using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PawnshopCompanion
{
    public sealed class CompanionPanelResize : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
    {
        internal CompanionRuntime owner;
        internal RectTransform target;
        private Vector2 startPointer,startSize,startPosition,screenScale;
        public void OnBeginDrag(PointerEventData data)
        {
            startPointer=data.position;startSize=target.rect.size;startPosition=target.anchoredPosition;
            screenScale=new Vector2(Mathf.Max(0.01f,target.lossyScale.x),Mathf.Max(0.01f,target.lossyScale.y));
        }
        public void OnDrag(PointerEventData data)
        {
            Vector2 delta=data.position-startPointer;
            owner.ResizeCompanionPanel(startSize+new Vector2(delta.x/screenScale.x,-delta.y/screenScale.y));
            Vector2 resized=target.rect.size-startSize;
            target.anchoredPosition=startPosition+new Vector2(resized.x*target.pivot.x,-resized.y*(1-target.pivot.y));
        }
        public void OnEndDrag(PointerEventData data){owner.SaveCompanionPanel();}
    }

    public sealed partial class CompanionRuntime
    {
        private float panelWidth=470,panelHeight=700,panelScale=1,panelSaveAt;
        private bool panelDirty,toolsExpanded=true;
        private const float ExpandedPanelMinHeight=670;
        private int panelTab;
        private ConfigEntry<float> panelWidthSetting,panelHeightSetting,panelScaleSetting;
        private ConfigEntry<bool> panelToolsSetting;
        private CustomUIElement panelScaleLabel,panelScaleSlider;

        private void InitCompanionPanelSettings()
        {
            ModConfig config=ModConfig.For("pawnshop_companion");
            panelWidthSetting=config.Define("panel_width",470f,"伙伴显示",()=>"面板宽度",360f,900f);
            panelHeightSetting=config.Define("panel_height",700f,"伙伴显示",()=>"面板高度",340f,1000f);
            panelScaleSetting=config.Define("panel_scale",1f,"伙伴显示",()=>"面板缩放",0.8f,1.4f);
            panelToolsSetting=config.Define("panel_tools",true,"伙伴显示",()=>"展开工具区");
            toolsExpanded=panelToolsSetting.Value;
            panelWidth=panelWidthSetting.Value;panelHeight=panelHeightSetting.Value;panelScale=panelScaleSetting.Value;
        }
        internal void ResizeCompanionPanel(Vector2 size)
        {
            RectTransform canvasRect=petCanvas!=null?(RectTransform)petCanvas.transform:null;
            float screenWidth=canvasRect!=null && canvasRect.rect.width>1?canvasRect.rect.width:1920;
            float screenHeight=canvasRect!=null && canvasRect.rect.height>1?canvasRect.rect.height:1080;
            panelWidth=Mathf.Clamp(size.x,360,Mathf.Max(360,Mathf.Min(900,(screenWidth-32)/panelScale)));
            float minimum=toolsExpanded?ExpandedPanelMinHeight:340;
            panelHeight=Mathf.Clamp(size.y,minimum,Mathf.Max(minimum,Mathf.Min(1000,(screenHeight-32)/panelScale)));
            if(petMenu!=null && petMenu.IsAlive)petMenu.SetSize(panelWidth,panelHeight);
            panelDirty=true;panelSaveAt=Time.unscaledTime+0.65f;
        }
        internal void SaveCompanionPanel()
        {
            if(!panelDirty || panelWidthSetting==null)return;
            panelDirty=false;panelWidthSetting.Set(panelWidth);panelHeightSetting.Set(panelHeight);panelScaleSetting.Set(panelScale);
        }
        private void SetPanelScale(float percentage)
        {
            panelScale=Mathf.Clamp(percentage/100,0.8f,1.4f);
            if(petMenu!=null && petMenu.IsAlive)petMenu.Rect.localScale=Vector3.one*panelScale;
            ResizeCompanionPanel(new Vector2(panelWidth,panelHeight));
            if(panelScaleLabel!=null && panelScaleLabel.IsAlive)panelScaleLabel.SetText("面板缩放："+Mathf.RoundToInt(panelScale*100)+"%");
            if(panelScaleSlider!=null && panelScaleSlider.IsAlive && panelScaleSlider.slider!=null)panelScaleSlider.slider.SetValueWithoutNotify(panelScale*100);
        }
        private void SelectPanelTab(int tab)
        {
            panelTab=tab;if(petMenu==null || !petMenu.IsAlive)return;
            string[] ids={"actions","display","permissions"};string[] captions={"帮忙","显示","权限"};
            for(int i=0;i<ids.Length;i++)
            {
                CustomUIElement section=petMenu.Get("panel_"+ids[i]);if(section!=null)section.SetVisible(i==tab);
                CustomUIElement button=petMenu.Get("tab_"+ids[i]);if(button!=null)button.SetText(i==tab?"【"+captions[i]+"】":captions[i]);
            }
        }
        private void FoldPanelTools()
        {
            if(petMenu==null || !petMenu.IsAlive)return;
            Vector2 position=petMenu.Rect.anchoredPosition;
            string draft=inputElement!=null && inputElement.IsAlive?inputElement.GetString():"";
            toolsExpanded=!toolsExpanded;
            panelToolsSetting.Set(toolsExpanded);
            if(toolsExpanded && panelHeight<ExpandedPanelMinHeight)panelHeight=ExpandedPanelMinHeight;
            petMenu.Close();petMenu=null;
            BuildUnifiedPanel();petMenu.Rect.anchoredPosition=position;
            if(inputElement!=null && inputElement.IsAlive)inputElement.SetString(draft,false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(petMenu.Rect);
        }
        private void RefreshPanelStatus()
        {
            if(petMenu==null || !petMenu.IsAlive)return;
            CustomUIElement button=petMenu.Get("start_stop");string caption=active?"停止陪玩":"开始陪玩";
            if(button!=null && button.GetText()!=caption)button.SetText(caption);
        }
        private void ToggleUnifiedPanel()
        {
            if(petMenu!=null && petMenu.IsAlive && petMenu.IsVisible){petMenu.Hide();ReleaseTyping();return;}
            OpenUnifiedPanel(false);
        }
        private void OpenUnifiedPanel(bool focusInput)
        {
            petHidden=false;if(petCanvas==null)CreatePetOverlay();else petCanvas.SetActive(true);
            if(petMenu==null || !petMenu.IsAlive)BuildUnifiedPanel();else petMenu.Show();
            if(petSpeech!=null)petSpeech.gameObject.SetActive(false);
            if(focusInput && inputElement!=null && inputElement.input!=null)inputElement.input.ActivateInputField();
        }
        private void BuildUnifiedPanel()
        {
            if(toolsExpanded && panelHeight<ExpandedPanelMinHeight)panelHeight=ExpandedPanelMinHeight;
            CustomUIBuilder ui=CustomUIManager.Window("pawnshop_companion.panel","当铺搭子").SetSize(panelWidth,panelHeight)
                .SetPosition(PetPopupPosition(panelWidth*panelScale,panelHeight*panelScale)).SetDraggable(true).SetCloseOnEscape(true)
                .OnClose(()=>{ReleaseTyping();SaveCompanionPanel();});
            ui.AddLabel(partner!=null?partner+"在店里":networkStatus,"status");
            ui.AddButton(toolsExpanded?"收起工具 ▲":"展开工具 ▼",FoldPanelTools,"tools_toggle");
            ui.BeginScrollFlexible(130).AddLabel(chat.Count>0?String.Join("\n\n",chat.ToArray()):"想聊什么就说，也可以在下面请伙伴搭把手。","chat").End();
            ui.BeginRow().AddInput("跟伙伴说点什么……","",null,"message");ui.Last.SetFlexible(1,0);
            ui.AddButton("发送",SendPlayer);ui.Last.SetFixedWidth(64);ui.End();
            ui.BeginRow().AddButton(active?"停止陪玩":"开始陪玩",()=>{if(active)StopCompanionship();else StartCompanionship();},"start_stop")
                .AddButton("收起伙伴",()=>{petHidden=true;if(petCanvas!=null)petCanvas.SetActive(false);petMenu.Hide();ReleaseTyping();}).End();

            if(toolsExpanded){
            ui.BeginPanel(-1,230,TextAnchor.UpperLeft,4,4,"tools_section");
            ui.BeginRow().AddButton("帮忙",()=>SelectPanelTab(0),"tab_actions")
                .AddButton("显示",()=>SelectPanelTab(1),"tab_display")
                .AddButton("权限",()=>SelectPanelTab(2),"tab_permissions").End();
            ui.BeginScroll(180);
            ui.BeginPanel(-1,-1,TextAnchor.UpperLeft,2,4,"panel_actions");
            ui.BeginRow().AddButton("清点所有箱子",()=>MenuRequest("帮我看看所有仓库和箱子，简单说说有什么。"))
                .AddButton("整理仓库",()=>MenuRequest("帮我整理一下选中的仓库。")).End();
            ui.BeginRow().AddButton("检查供电",()=>MenuRequest("帮我看看机器供电和电池情况。"))
                .AddButton("看看机器",()=>MenuRequest("帮我看看机器有哪些可操作的选项，先不要改设置。")).End();
            ui.BeginRow().AddButton("聊聊今天的行情",()=>MenuRequest("一起翻翻日历，看看今天有什么行情和要留意的事。"))
                .AddButton("看看这档的玩法",()=>MenuRequest("看看这档的天赋、难度和阵营声望，咱们聊聊怎么玩。")).End();
            ui.AddDropdown(new List<string>{"店铺仓库","后屋仓库"},storageIndex,index=>{storageIndex=index;dirty=true;itemRefs.Clear();expectedParents.Clear();});
            ui.AddLabel("需要找某样东西、投料或换电池，也可以直接在上面说。");ui.End();

            ui.BeginPanel(-1,-1,TextAnchor.UpperLeft,2,4,"panel_display");
            List<string> keys=new List<string>(),names=new List<string>();int selected=0;
            string[] figures={"gpt","deepseek","gemini","claude","grok","glm","kimi","minimax","qwen"};
            string[] figureNames={"GPT · 白龙娘","DeepSeek · 鲸鱼娘","Gemini","Claude","Grok","GLM","Kimi","MiniMax","Qwen"};
            for(int i=0;i<figures.Length;i++)if(portraits.ContainsKey(figures[i])){if(figures[i]==avatarKey)selected=keys.Count;keys.Add(figures[i]);names.Add(figureNames[i]);}
            if(keys.Count>0)ui.AddDropdown(names,selected,index=>{manualAvatar=true;SetPetPortrait(keys[index]);});
            ui.AddLabel("角色大小："+Mathf.RoundToInt(petScale*100)+"%","scale_label");
            ui.AddSlider(MinPetScale*100,MaxPetScale*100,petScale*100,value=>SetPetScale(value/100),true,"scale_slider");
            ui.AddLabel("面板缩放："+Mathf.RoundToInt(panelScale*100)+"%","panel_scale_label");
            ui.AddSlider(80,140,panelScale*100,SetPanelScale,true,"panel_scale_slider");
            ui.AddButton("恢复默认大小",()=>{SetPetScale(DefaultPetScale);SetPanelScale(100);ResizeCompanionPanel(new Vector2(470,700));});
            ui.AddLabel("角色可用 Ctrl + 滚轮缩放；面板可拖右下角调整宽高。");
            ui.AddLabel("首次连接：运行接入包里的「首次连接」，复制连接码。");
            ui.AddInput("在这里粘贴连接码",pairingSetting.Value,null,"pairing_code");
            ui.AddButton("保存连接码",SavePairingFromPanel);ui.End();

            ui.BeginPanel(-1,-1,TextAnchor.UpperLeft,2,4,"panel_permissions");
            ui.AddToggle("查看仓库和所有箱子",inventoryReadAllowed,value=>{inventoryReadAllowed=value;dirty=true;});
            ui.AddToggle("操作物品和机器",interactionsAllowed,value=>{interactionsAllowed=value;dirty=true;if(!value){itemOptions.Clear();machineControls.Clear();machineStores.Clear();}});
            ui.AddToggle("整理选中的仓库",sortAllowed,value=>{sortAllowed=value;dirty=true;});
            ui.AddToggle("查看供电、换现有电池",powerAllowed,value=>{powerAllowed=value;dirty=true;itemRefs.Clear();expectedParents.Clear();});
            ui.AddLabel("你正在拿东西或整理时，伙伴会先让开。");ui.End();
            ui.End().End();
            }
            petMenu=ui.Show();petMenu.Rect.localScale=Vector3.one*panelScale;
            logElement=petMenu.Get("chat");statusElement=petMenu.Get("status");inputElement=petMenu.Get("message");
            petScaleLabel=petMenu.Get("scale_label");petScaleSlider=petMenu.Get("scale_slider");
            panelScaleLabel=petMenu.Get("panel_scale_label");panelScaleSlider=petMenu.Get("panel_scale_slider");
            if(logElement!=null && logElement.text!=null){logElement.text.richText=false;logElement.text.fontSize=18;}
            if(inputElement!=null && inputElement.input!=null)
            {
                inputElement.input.characterLimit=1000;inputElement.input.onSubmit.AddListener(unused=>SendPlayer());
                inputElement.input.onSelect.AddListener(unused=>{typing=true;if(StoreUIManager.Instance!=null)StoreUIManager.Instance.StartTyping();});
                inputElement.input.onDeselect.AddListener(unused=>ReleaseTyping());
            }
            CustomUIElement pairingInput=petMenu.Get("pairing_code");
            if(pairingInput!=null && pairingInput.input!=null)
            {
                pairingInput.input.characterLimit=80;
                pairingInput.input.onSelect.AddListener(unused=>{typing=true;if(StoreUIManager.Instance!=null)StoreUIManager.Instance.StartTyping();});
                pairingInput.input.onDeselect.AddListener(unused=>ReleaseTyping());
            }
            if(toolsExpanded)SelectPanelTab(panelTab);AddPanelResizeHandle();lastStatus="";RefreshStatus();
        }
        private void AddPanelResizeHandle()
        {
            GameObject handle=new GameObject("拖动调整大小",typeof(RectTransform),typeof(Image),typeof(CompanionPanelResize));
            handle.transform.SetParent(petMenu.Rect,false);
            RectTransform rect=(RectTransform)handle.transform;rect.anchorMin=rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(1,0);
            rect.anchoredPosition=new Vector2(-3,3);rect.sizeDelta=new Vector2(24,24);handle.GetComponent<Image>().color=new Color(1,1,1,0.01f);
            CompanionPanelResize resize=handle.GetComponent<CompanionPanelResize>();resize.owner=this;resize.target=petMenu.Rect;
            for(int i=0;i<2;i++)
            {
                GameObject line=new GameObject("缩放角",typeof(RectTransform),typeof(Image));line.transform.SetParent(rect,false);
                RectTransform lr=(RectTransform)line.transform;lr.anchorMin=lr.anchorMax=new Vector2(1,0);lr.pivot=new Vector2(1,0);
                lr.anchoredPosition=new Vector2(-5,5);lr.sizeDelta=i==0?new Vector2(12,2):new Vector2(2,12);
                Image image=line.GetComponent<Image>();image.color=new Color32(182,151,191,230);image.raycastTarget=false;
            }
        }
    }
}
