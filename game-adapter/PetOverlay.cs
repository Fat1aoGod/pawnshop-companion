using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PawnshopCompanion
{
    public sealed class PetPointer : MonoBehaviour,IPointerClickHandler,IBeginDragHandler,IDragHandler,IEndDragHandler,IScrollHandler
    {
        internal CompanionRuntime owner;
        private bool dragged;
        public void OnPointerClick(PointerEventData data)
        {
            if(dragged){dragged=false;return;}
            if(owner!=null)owner.PetClicked(data.button==PointerEventData.InputButton.Right);
        }
        public void OnBeginDrag(PointerEventData data){dragged=true;}
        public void OnDrag(PointerEventData data)
        {
            RectTransform rect=transform as RectTransform;Canvas canvas=GetComponentInParent<Canvas>();
            if(rect!=null && canvas!=null)rect.anchoredPosition+=data.delta/canvas.scaleFactor;
        }
        public void OnEndDrag(PointerEventData data)
        {
            if(owner!=null)owner.ClampPetPosition();
        }
        public void OnScroll(PointerEventData data)
        {
            if(owner!=null && (Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))
                owner.AdjustPetScale(Mathf.Sign(data.scrollDelta.y)*0.05f);
        }
    }

    public sealed partial class CompanionRuntime
    {
        private GameObject petCanvas;
        private Image petImage;
        private TextMeshProUGUI petSpeech,petName;
        private readonly Dictionary<string,Sprite> portraits=new Dictionary<string,Sprite>();
        private CustomUIWindow petMenu;
        private CustomUIElement petScaleLabel,petScaleSlider;
        private string avatarKey="gpt",petStatus="";
        private bool petHidden,manualAvatar;
        private float speechUntil,nextPetCheck;
        private const float DefaultPetScale=1.45f,MinPetScale=0.75f,MaxPetScale=2.2f;
        private float petScale=DefaultPetScale,petScaleSaveAt;
        private bool petScaleDirty;
        private ConfigEntry<float> petScaleSetting;

        private void InitPetAssets()
        {
            InitCompanionPanelSettings();
            petScaleSetting=ModConfig.For("pawnshop_companion").Define("pet_scale",DefaultPetScale,"伙伴显示",()=>"角色大小",MinPetScale,MaxPetScale,()=>"在右键菜单中调整，或指向立绘按住 Ctrl 滚动滚轮。");
            petScale=Mathf.Clamp(petScaleSetting.Value,MinPetScale,MaxPetScale);
            petScaleSetting.OnChanged+=OnPetScaleChanged;
            ModHook.OnModAssetsLoaded+=PetAssetsLoaded;
            foreach(LoadedMod loaded in ModLoader.Mods)if(loaded.Manifest.ID=="pawnshop_companion"){PetAssetsLoaded(loaded);break;}
        }
        private void PetAssetsLoaded(LoadedMod loaded)
        {
            if(loaded.Manifest.ID!="pawnshop_companion")return;
            foreach(KeyValuePair<string,Texture2D> pair in loaded.Textures)
            {
                string key=pair.Key.ToLowerInvariant();
                if(key!="gpt" && key!="deepseek" && key!="claude" && key!="gemini" && key!="grok" && key!="glm" && key!="kimi" && key!="minimax" && key!="qwen")continue;
                Texture2D texture=pair.Value;Sprite old;
                if(portraits.TryGetValue(key,out old)){if(old.texture==texture)continue;Destroy(old);}
                texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
                portraits[key]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100,0,SpriteMeshType.FullRect);
            }
            SetPetPortrait(avatarKey);
        }
        private void UpdatePetOverlay()
        {
            if(petScaleDirty && Time.unscaledTime>=petScaleSaveAt)SavePetScale();
            if(panelDirty && Time.unscaledTime>=panelSaveAt)SaveCompanionPanel();
            if(Time.unscaledTime>=nextPetCheck)
            {
                nextPetCheck=Time.unscaledTime+0.5f;
                if(InStore() && petCanvas==null)CreatePetOverlay();
                else if(!InStore() && petCanvas!=null)DestroyPetOverlay();
            }
            if(speechUntil>0 && Time.unscaledTime>=speechUntil)
            {speechUntil=0;if(petSpeech!=null)petSpeech.gameObject.SetActive(false);}
        }
        private TextMeshProUGUI PetText(string name,RectTransform parent,Vector2 position,Vector2 size,float fontSize)
        {
            GameObject root=new GameObject(name,typeof(RectTransform));root.transform.SetParent(parent,false);
            RectTransform rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=new Vector2(0.5f,1);rect.pivot=new Vector2(0.5f,0);
            rect.anchoredPosition=position;rect.sizeDelta=size;
            TextMeshProUGUI text=root.AddComponent<TextMeshProUGUI>();
            TMP_Text original=StoreUIManager.Instance!=null && StoreUIManager.Instance.storeCalendar!=null?StoreUIManager.Instance.storeCalendar.dayTMP:null;
            if(original!=null){text.font=original.font;text.fontSharedMaterial=original.fontSharedMaterial;}
            text.fontSize=fontSize;text.richText=false;text.raycastTarget=false;text.enableWordWrapping=true;
            text.color=new Color(0.96f,0.91f,0.98f,1);text.outlineColor=new Color32(49,35,59,255);text.outlineWidth=0.2f;
            text.alignment=TextAlignmentOptions.BottomLeft;return text;
        }
        private void CreatePetOverlay()
        {
            petCanvas=new GameObject("PawnshopCompanion.Pet",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            Canvas canvas=petCanvas.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1800;
            CanvasScaler scaler=petCanvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=0.5f;
            GameObject portrait=new GameObject("伙伴",typeof(RectTransform),typeof(Image),typeof(PetPointer));portrait.transform.SetParent(petCanvas.transform,false);
            RectTransform rect=(RectTransform)portrait.transform;rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=new Vector2(0.5f,0);
            rect.sizeDelta=new Vector2(145,248)*petScale;rect.anchoredPosition=new Vector2(36+rect.sizeDelta.x*0.5f,15);
            petImage=portrait.GetComponent<Image>();petImage.preserveAspect=true;petImage.raycastTarget=true;
            portrait.GetComponent<PetPointer>().owner=this;
            petSpeech=PetText("伙伴的话",rect,new Vector2(126,24),new Vector2(330,134),19);
            petSpeech.gameObject.SetActive(false);
            petName=PetText("伙伴名字",rect,new Vector2(45,-6),new Vector2(250,28),17);
            ApplyPetScale();
            SetPetPortrait(avatarKey);petStatus="";RefreshPetStatus();
            petCanvas.SetActive(!petHidden);
        }
        private void SetPetPortrait(string key)
        {
            if(!String.IsNullOrEmpty(key))avatarKey=key.ToLowerInvariant();
            Sprite sprite;
            if(petImage!=null && portraits.TryGetValue(avatarKey,out sprite))
            {petImage.sprite=sprite;petImage.color=Color.white;petImage.alphaHitTestMinimumThreshold=sprite.texture.isReadable?0.08f:0;}
            else if(petImage!=null)petImage.color=Color.clear;
        }
        private void RefreshPetStatus()
        {
            string status=partner!=null?partner:active?"等伙伴来店里":"右键伙伴 · 开始陪玩";
            if(status!=petStatus && petName!=null){petName.text=status;petStatus=status;}
        }
        private void ShowPetSpeech(string who,string message)
        {
            if(petSpeech==null)return;
            if(petMenu!=null && petMenu.IsAlive && petMenu.IsVisible){petSpeech.gameObject.SetActive(false);return;}
            string caption=(String.IsNullOrEmpty(who)?"你":who)+"："+message;
            if(caption.Length>180)caption=caption.Substring(0,179)+"…";
            petSpeech.text=caption;petSpeech.gameObject.SetActive(true);speechUntil=Time.unscaledTime+Mathf.Clamp(caption.Length*0.16f,8,28);
        }
        private void DestroyPetOverlay()
        {
            SaveCompanionPanel();if(petMenu!=null && petMenu.IsAlive)petMenu.Close();
            petMenu=null;petScaleLabel=null;petScaleSlider=null;panelScaleLabel=null;panelScaleSlider=null;
            if(petCanvas!=null)Destroy(petCanvas);petCanvas=null;petImage=null;petSpeech=null;petName=null;ReleaseTyping();
        }
        private void DisposePetAssets()
        {
            SavePetScale();if(petScaleSetting!=null)petScaleSetting.OnChanged-=OnPetScaleChanged;
            ModHook.OnModAssetsLoaded-=PetAssetsLoaded;DestroyPetOverlay();
            foreach(Sprite sprite in portraits.Values)if(sprite!=null)Destroy(sprite);portraits.Clear();
        }
        internal void PetClicked(bool rightButton){if(rightButton)TogglePetMenu();else OpenComposer();}
        internal void AdjustPetScale(float delta){SetPetScale(petScale+delta);}
        private void SetPetScale(float value)
        {
            petScale=Mathf.Clamp(Mathf.Round(value*100)/100,MinPetScale,MaxPetScale);ApplyPetScale();
            petScaleDirty=true;petScaleSaveAt=Time.unscaledTime+0.65f;
        }
        private void OnPetScaleChanged(float value){petScale=Mathf.Clamp(value,MinPetScale,MaxPetScale);ApplyPetScale();}
        private void SavePetScale()
        {
            if(!petScaleDirty || petScaleSetting==null)return;
            petScaleDirty=false;petScaleSetting.Set(petScale);
        }
        private void ApplyPetScale()
        {
            if(petImage!=null){petImage.rectTransform.sizeDelta=new Vector2(145,248)*petScale;ClampPetPosition();}
            if(petScaleLabel!=null && petScaleLabel.IsAlive)petScaleLabel.SetText("角色大小："+Mathf.RoundToInt(petScale*100)+"%");
            if(petScaleSlider!=null && petScaleSlider.IsAlive && petScaleSlider.slider!=null)petScaleSlider.slider.SetValueWithoutNotify(petScale*100);
        }
        internal void ClampPetPosition()
        {
            if(petImage==null || petCanvas==null)return;
            RectTransform rect=petImage.rectTransform,canvasRect=(RectTransform)petCanvas.transform;
            float width=canvasRect.rect.width>1?canvasRect.rect.width:1920,height=canvasRect.rect.height>1?canvasRect.rect.height:1080;
            float half=rect.sizeDelta.x*0.5f;
            rect.anchoredPosition=new Vector2(Mathf.Clamp(rect.anchoredPosition.x,half+12,Mathf.Max(half+12,width-half-12)),
                Mathf.Clamp(rect.anchoredPosition.y,10,Mathf.Max(10,height-rect.sizeDelta.y-50)));
        }
        private Vector2 PetPopupPosition(float width,float height)
        {
            if(petImage==null || petCanvas==null)return new Vector2(-650,-180);
            RectTransform canvasRect=(RectTransform)petCanvas.transform,pet=petImage.rectTransform;
            float canvasWidth=canvasRect.rect.width>1?canvasRect.rect.width:1920,canvasHeight=canvasRect.rect.height>1?canvasRect.rect.height:1080;
            float x=pet.anchoredPosition.x+pet.sizeDelta.x*0.5f+width*0.5f+20;
            if(x+width*0.5f>canvasWidth-12)x=pet.anchoredPosition.x-pet.sizeDelta.x*0.5f-width*0.5f-20;
            float y=pet.anchoredPosition.y+pet.sizeDelta.y*0.5f;
            x=Mathf.Clamp(x,width*0.5f+12,canvasWidth-width*0.5f-12);
            y=Mathf.Clamp(y,height*0.5f+12,canvasHeight-height*0.5f-12);
            return new Vector2(x-canvasWidth*0.5f,y-canvasHeight*0.5f);
        }

        private void StartCompanionship()
        {
            if(bridgeToken.Length==0){networkStatus="先在「显示」里填写本机连接码";RefreshStatus();ShowPetSpeech("提示",networkStatus);return;}
            if(!InStore())return;active=true;disconnect=false;dirty=true;networkStatus="正在接通本机伙伴";RefreshStatus();
        }
        private void StopCompanionship()
        {
            active=false;disconnect=true;binding=null;partner=null;networkStatus="陪玩已停止";RefreshStatus();
        }
        private void MenuRequest(string message)
        {
            if(!active)StartCompanionship();
            if(!active)return;
            if(events.Count>=32){ShowPetSpeech("提示","本机连接还没接通，稍等一下。");return;}
            events.Add(new JObject{{"id",Guid.NewGuid().ToString("N")},{"type","player_message"},{"text",message}});
            AddChat("你",message);dirty=true;
        }
        private void TogglePetMenu(){ToggleUnifiedPanel();}

        private void OpenComposer(){OpenUnifiedPanel(true);}



    }
}
