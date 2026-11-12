using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace PawnshopCompanion
{
    public sealed partial class CompanionRuntime
    {
        private bool interactionsAllowed=true;
        private readonly Dictionary<string,GameItem> catalogObjects=new Dictionary<string,GameItem>();
        private readonly Dictionary<string,ItemOption> itemOptions=new Dictionary<string,ItemOption>();
        private readonly Dictionary<string,MachineControl> machineControls=new Dictionary<string,MachineControl>();
        private readonly Dictionary<string,MachineStorage> machineStores=new Dictionary<string,MachineStorage>();
        private string confirmationId,confirmationText;
        private Action confirmationYes,confirmationNo;

        private sealed class ItemOption
        {
            internal GameItem item;
            internal GameInventory parent;
            internal string verb,label;
            internal ItemOption(GameItem value,string action,string caption){item=value;parent=value.parentInventory;verb=action;label=caption;}
        }
        private sealed class MachineControl
        {
            internal GameItem owner;
            internal PixelWindow window;
            internal PixelElement element;
            internal string label;
        }
        private sealed class MachineStorage
        {
            internal GameItem owner;
            internal GameInventory inventory;
            internal PixelWindow window;
        }
        private GameItem InteractionItem(string reference)
        {
            GameItem item;
            return reference!=null && catalogObjects.TryGetValue(reference,out item) && item.parentInventory!=null && GeneralHelper.IsItemEffectivelyOwned(item)?item:null;
        }
        private void ClearInteractions()
        {
            catalogObjects.Clear();itemOptions.Clear();machineControls.Clear();machineStores.Clear();
            confirmationId=null;confirmationYes=null;confirmationNo=null;confirmationText=null;
        }
        private void AddOption(JArray options,GameItem item,string verb,string label,bool visible,bool enabled)
        {
            if(!visible)return;
            string id=Guid.NewGuid().ToString("N");
            itemOptions[id]=new ItemOption(item,verb,label);
            options.Add(new JObject{{"option_id",id},{"label",label},{"kind",verb},{"enabled",enabled}});
        }
        private bool CanItemOption(GameItem item,string verb)
        {
            switch(verb)
            {
                case "open":return item.contentWindow!=null && !item.contentWindow.isLocked && GeneralHelper.IsItemOwned(item);
                case "close":return item.contentWindow!=null && item.contentWindow.IsVisible();
                case "examine":return item.examineWindow!=null && !item.examineWindow.isLocked;
                case "activate":return item.MayActivateSlotItem() && item.CanActivateSlotItem();
                case "configure":return item.MayConfigureSlotItem() && item.CanConfigureSlotItem();
                case "toggle":return item.MayToggleSlotItem() && item.CanToggleSlotItem();
                case "use":case "equip":return item.MaySelectSlotItem() && item.CanSelectSlotItem();
                case "unload":return CanItemOption(item,"open") && !item.IsTag("IS_UNLOAD_DISABLED_TAG");
                case "pin":return ItemPinHelper.MayPin(item);
            }
            return false;
        }
        private JObject InspectItemOptions(JObject command)
        {
            GameItem item=InteractionItem((string)command["item_ref"]);
            if(item==null)return Ack(command,"unavailable","先清点一次仓库，取得这件物品的当前位置。");
            if(!inventoryReadAllowed)return Ack(command,"blocked","物品还没有交给伙伴查看。");
            if(itemOptions.Count>256)itemOptions.Clear();
            JArray options=new JArray();
            AddOption(options,item,"open","打开内容 / 机器面板",item.contentWindow!=null,CanItemOption(item,"open"));
            AddOption(options,item,"close","收起内容面板",item.contentWindow!=null && item.contentWindow.IsVisible(),true);
            AddOption(options,item,"examine","打开查看详情",item.examineWindow!=null,CanItemOption(item,"examine"));
            AddOption(options,item,"activate","激活 / 使用",item.MayActivateSlotItem(),CanItemOption(item,"activate"));
            AddOption(options,item,"configure","配置",item.MayConfigureSlotItem(),CanItemOption(item,"configure"));
            AddOption(options,item,"toggle","切换开关 / 状态",item.MayToggleSlotItem(),CanItemOption(item,"toggle"));
            AddOption(options,item,"use","拿起使用，选择目标",item.MaySelectSlotItem(),CanItemOption(item,"use"));
            AddOption(options,item,"equip","装备",item.MaySelectSlotItem(),CanItemOption(item,"equip"));
            AddOption(options,item,"unload","卸出全部内容",item.contentWindow!=null,CanItemOption(item,"unload"));
            AddOption(options,item,"pin","固定 / 取消固定",ItemPinHelper.MayPin(item),true);
            JObject result=Ack(command,"observed",Plain(item.GetDisplayName())+"当前的原生物品选项。");
            result["detail"]=new JObject{{"item_ref",(string)command["item_ref"]},{"name",Plain(item.GetDisplayName())},{"options",options}};
            return result;
        }
        private JObject ApplyItemOption(JObject command)
        {
            ItemOption option;string optionId=(string)command["option_id"];
            if(optionId==null || !itemOptions.TryGetValue(optionId,out option))return Ack(command,"unavailable","选项已经变过，先重新看这件物品的菜单。");
            GameItem item=InteractionItem((string)command["item_ref"]);
            if(item==null || item!=option.item || item.parentInventory!=option.parent)return Ack(command,"unavailable","物品位置变了，先重新看菜单。");
            if(!CanItemOption(item,option.verb))return Ack(command,"blocked","游戏现在不允许这个选项。");
            string status="invoked";
            switch(option.verb)
            {
                case "open":
                    ItemMouseDoubleClickHandler.current.OpenContentAction(item,new Vector2(Screen.width*0.62f,Screen.height*0.65f));
                    status=item.contentWindow.IsVisible()?"applied":"blocked";break;
                case "close":item.CloseContentWindow(false);status=!item.contentWindow.IsVisible()?"applied":"blocked";break;
                case "examine":
                    ItemMouseDoubleClickHandler.current.OpenExamineAction(item,new Vector2(Screen.width*0.62f,Screen.height*0.65f));
                    status=item.examineWindow.IsVisible()?"applied":"blocked";break;
                case "activate":item.ActivateSlotItem();break;
                case "configure":item.ConfigureSlotItem();break;
                case "toggle":item.ToggleSlotItem();break;
                case "use":ItemSelectHandler.current.TrySelectItem(item);break;
                case "equip":ItemSelectHandler.current.TryEquipItem(item);break;
                case "unload":item.TryUnloadAll(true);break;
                case "pin":status=ItemPinHelper.TogglePinned(item)?"applied":"blocked";break;
            }
            itemOptions.Remove(optionId);machineControls.Clear();machineStores.Clear();MarkInventoryDirty();
            JObject result=Ack(command,status,"对「"+Plain(item.GetDisplayName())+"」用了原生「"+option.label+"」。");
            result["detail"]=new JObject{{"item_ref",(string)command["item_ref"]},{"option",option.verb},{"confirmation",CurrentConfirmation()}};
            return result;
        }
        private static string ControlLabel(PixelElement element)
        {
            MachineryButton button=element as MachineryButton;
            if(button!=null)return Plain(!String.IsNullOrEmpty(button.text)?button.text:!String.IsNullOrEmpty(button.description)?button.description:button.icon!=null?button.icon.name:"按钮");
            MachineryRadioButton radio=element as MachineryRadioButton;
            if(radio!=null)return Plain(!String.IsNullOrEmpty(radio.text)?radio.text:radio.description);
            MachinerySlider slider=element as MachinerySlider;if(slider!=null)return Plain(slider.description);
            MachineryRangeSlider range=element as MachineryRangeSlider;if(range!=null)return Plain(range.description);
            return "";
        }
        private JObject InspectMachineControls(JObject command)
        {
            GameItem item=InteractionItem((string)command["item_ref"]);
            if(item==null)return Ack(command,"unavailable","先清点一次仓库，再看看这台机器。");
            PixelWindow content=item.contentWindow;
            if(content==null)return Ack(command,"unavailable","这件物品没有内容面板。");
            machineControls.Clear();machineStores.Clear();
            JArray controls=new JArray(),storages=new JArray(),labels=new JArray();
            HashSet<PixelElement> seen=new HashSet<PixelElement>();
            TreeNodeRender[] nodes=content.handler.GetComponentsInChildren<TreeNodeRender>(true);
            foreach(TreeNodeRender node in nodes)
            {
                PixelElement element=node.element;if(element==null || !seen.Add(element))continue;
                if(!content.IsVisible() || element.rectTransform==null || !element.rectTransform.gameObject.activeInHierarchy)continue;
                GameInventory storage=element as GameInventory;
                if(storage!=null)
                {
                    string reference="slot-"+Guid.NewGuid().ToString("N");
                    machineStores[reference]=new MachineStorage{owner=item,inventory=storage,window=content};
                    JArray contents=new JArray();foreach(GameItem child in storage.childItems)
                    {
                        string childRef="item-"+child.GetUniqueID();catalogObjects[childRef]=child;
                        contents.Add(new JObject{{"ref",childRef},{"name",Plain(child.GetDisplayName())},{"quantity",child.unitCount}});
                    }
                    storages.Add(new JObject{{"storage_ref",reference},{"label",Plain(storage.identifier)},{"items",contents},
                        {"insert_locked",storage.IsInsertLocked()},{"remove_locked",storage.IsRemoveLocked()}});
                    continue;
                }
                bool supported=element is MachineryButton || element is MachineryRadioButton || element is MachinerySlider || element is MachineryRangeSlider || element is MachineryTabs;
                if(!supported)continue;
                string id="control-"+Guid.NewGuid().ToString("N"),label=ControlLabel(element);
                if(String.IsNullOrEmpty(label))label=element is MachineryTabs?"切换页签":"机器控件";
                machineControls[id]=new MachineControl{owner=item,window=content,element=element,label=label};
                JObject value=new JObject{{"control_ref",id},{"label",label}};
                SelectableElement selectable=element as SelectableElement;
                if(selectable!=null){value["enabled"]=!selectable.isDisabled && !selectable.isHidden;value["hold_seconds"]=selectable.requiresHold?selectable.holdDuration:0;}
                MachineryRadioButton radio=element as MachineryRadioButton;
                if(radio!=null){value["kind"]="toggle";value["value"]=radio.isOn;}
                else if(element is MachineryButton)value["kind"]="button";
                MachinerySlider slider=element as MachinerySlider;
                if(slider!=null){value["kind"]="slider";value["enabled"]=!slider.isDisabled;value["value"]=slider.value;value["min"]=slider.min;value["max"]=slider.max;value["step"]=slider.step;}
                MachineryRangeSlider range=element as MachineryRangeSlider;
                if(range!=null){value["kind"]="range";value["enabled"]=!range.isDisabled;value["low"]=range.low;value["high"]=range.high;value["min"]=range.min;value["max"]=range.max;}
                MachineryTabs tabs=element as MachineryTabs;
                if(tabs!=null){value["kind"]="tabs";value["selected"]=tabs.selected;value["count"]=tabs.count;value["enabled"]=true;}
                controls.Add(value);
            }
            if(content.IsVisible())foreach(TMP_Text label in content.handler.GetComponentsInChildren<TMP_Text>(false))
            {string line=Plain(label.text);if(line.Length>0 && labels.Count<60)labels.Add(line.Length>300?line.Substring(0,300):line);}
            JObject result=Ack(command,"observed",Plain(item.GetDisplayName())+"的当前面板。");
            result["detail"]=new JObject{{"item_ref",(string)command["item_ref"]},{"window_open",content.IsVisible()},
                {"text",labels},{"controls",controls},{"storages",storages}};return result;
        }
        private JObject ApplyMachineControl(JObject command)
        {
            string id=(string)command["control_ref"];MachineControl control;
            if(id==null || !machineControls.TryGetValue(id,out control))return Ack(command,"unavailable","控件变了，先重新看机器面板。");
            if(!GeneralHelper.IsItemEffectivelyOwned(control.owner) || control.owner.contentWindow!=control.window || !control.window.IsVisible() || control.element.rectTransform==null || !control.element.rectTransform.gameObject.activeInHierarchy)
                return Ack(command,"unavailable","原来的机器面板已经不在眼前了。");
            PixelElement element=control.element;SelectableElement selectable=element as SelectableElement;
            string liveLabel=ControlLabel(element);
            if(!String.IsNullOrEmpty(liveLabel) && liveLabel!=control.label)return Ack(command,"unavailable","按钮显示的操作变了，先重新看面板。");
            if(selectable!=null && (selectable.isDisabled || selectable.isHidden))return Ack(command,"blocked","游戏里这个按钮现在是不可用的。");
            if(selectable!=null && selectable.requiresHold)return Ack(command,"blocked","这个控件需要原生长按，暂时交给玩家操作。");
            MachinerySlider slider=element as MachinerySlider;MachineryRangeSlider range=element as MachineryRangeSlider;
            MachineryTabs tabs=element as MachineryTabs;
            if(slider!=null)
            {
                if(slider.isDisabled || command["value"]==null)return Ack(command,"blocked","需要一个可用的滑块数值。");
                float next=(float)command["value"];if(Single.IsNaN(next)||Single.IsInfinity(next)||next<slider.min||next>slider.max)return Ack(command,"blocked","数值超出了游戏滑块的范围。");
                slider.SetValue(next,true);if(slider.onReleased!=null)slider.onReleased(slider.value);
            }
            else if(range!=null)
            {
                if(range.isDisabled || command["low"]==null || command["high"]==null)return Ack(command,"blocked","需要可用的上下限。");
                float low=(float)command["low"],high=(float)command["high"];
                if(Single.IsNaN(low)||Single.IsNaN(high)||Single.IsInfinity(low)||Single.IsInfinity(high)||low<range.min||high>range.max||low>high)return Ack(command,"blocked","上下限超出了游戏允许的范围。");
                range.SetValues(low,high,true);if(range.onReleased!=null)range.onReleased(range.low,range.high);
            }
            else if(tabs!=null)
            {
                int index=(int?)command["index"]??-1;if(index<0 || index>=tabs.count)return Ack(command,"blocked","没有这个页签。");tabs.Select(index,true);
            }
            else if(selectable==null || !selectable.Select())return Ack(command,"blocked","按钮这次没有按下。");
            machineControls.Clear();machineStores.Clear();MarkInventoryDirty();
            JObject result=Ack(command,"invoked","操作了「"+control.label+"」。");
            result["detail"]=new JObject{{"confirmation",CurrentConfirmation()}};return result;
        }
        private GraphNodeStorage ResolveDestination(string reference)
        {
            EmporiumEntry entry=EmporiumEntry.Instance;
            if(reference=="storage-main")return entry.invElement;
            if(reference=="storage-back")return entry.backInvinvElement;
            MachineStorage slot;
            if(reference!=null && machineStores.TryGetValue(reference,out slot))
                return slot.owner.contentWindow==slot.window && GeneralHelper.IsItemEffectivelyOwned(slot.owner)?slot.inventory:null;
            GameItem box=InteractionItem(reference);
            return box!=null && !GeneralHelper.IsItemLocked(box) && !box.IsTag(ContainerHelper.PRE_CONTAINER_TAG) &&
                (!box.IsTag(ContainerHelper.TRACK_OPENED_TAG) || box.IsTag(ContainerHelper.OPENED_TAG)) &&
                box.contentWindow!=null && !box.contentWindow.isLocked?box.contentWindow:null;
        }
        private JObject MoveNativeItem(JObject command)
        {
            GameItem source=InteractionItem((string)command["item_ref"]);
            GraphNodeStorage target=ResolveDestination((string)command["destination_ref"]);
            if(source==null || target==null)return Ack(command,"unavailable","物品或目标位置找不到了，先重新看一眼。");
            if(!source.MayRemove() || source.parentInventory.IsRemoveLocked())return Ack(command,"blocked","游戏现在不允许取出这个物品。");
            int count=(int?)command["quantity"]??source.unitCount;
            if(count<1 || count>source.unitCount || count>source.MaxNumRemove())return Ack(command,"blocked","这次可移动的数量不够。");
            string name=Plain(source.GetDisplayName());
            int remaining=GraphUtils.TryAcceptAll(target,source,count),moved=count-remaining;
            if(moved>0){MarkInventoryDirty();itemOptions.Clear();machineControls.Clear();machineStores.Clear();}
            JObject result=Ack(command,moved>0?"applied":"blocked",moved>0?"按原生放置规则移动了 "+moved+" 个「"+name+"」。":"目标槽位、空间或物品规则不允许放入。");
            result["detail"]=new JObject{{"moved",moved},{"requested",count},{"remaining",remaining}};return result;
        }
        private JObject UseNativeItemOn(JObject command)
        {
            GameItem source=InteractionItem((string)command["source_ref"]),target=InteractionItem((string)command["target_ref"]);
            if(source==null || target==null || source==target)return Ack(command,"unavailable","这两件物品的位置变了。");
            if(!source.MayTarget(target)||!source.CanTarget(target))return Ack(command,"blocked","游戏里这件物品不能对这个目标使用。");
            string sourceName=Plain(source.GetDisplayName()),targetName=Plain(target.GetDisplayName());
            source.Target(target);MarkInventoryDirty();itemOptions.Clear();machineControls.Clear();machineStores.Clear();
            JObject result=Ack(command,"invoked","对「"+targetName+"」使用了「"+sourceName+"」。");
            result["detail"]=new JObject{{"confirmation",CurrentConfirmation()}};return result;
        }
        private JObject CurrentConfirmation()
        {
            ConfirmPanelUIManager panel=StoreUIManager.Instance!=null?StoreUIManager.Instance.confirmPanelUIManager:null;
            if(panel==null || panel.panel==null || !panel.panel.activeInHierarchy){confirmationId=null;return null;}
            string body=Plain(panel.bodyTMP!=null?panel.bodyTMP.text:"");
            if(confirmationId==null || body!=confirmationText || confirmationYes!=panel.yesAction || confirmationNo!=panel.noAction)
            {confirmationId=Guid.NewGuid().ToString("N");confirmationText=body;confirmationYes=panel.yesAction;confirmationNo=panel.noAction;}
            return new JObject{{"confirmation_id",confirmationId},{"text",body},
                {"yes",Plain(panel.yesTMP!=null?panel.yesTMP.text:"确认")},{"no",Plain(panel.noTMP!=null?panel.noTMP.text:"取消")}};
        }
        private JObject AnswerConfirmation(JObject command)
        {
            string requested=(string)command["confirmation_id"];
            JObject current=CurrentConfirmation();
            if(current==null || requested!=confirmationId)return Ack(command,"unavailable","原来的确认窗口已经变了。");
            ConfirmPanelUIManager panel=StoreUIManager.Instance.confirmPanelUIManager;
            if((bool?)command["confirm"]==true)panel.OnYesClick();else panel.OnNoClick();
            confirmationId=null;MarkInventoryDirty();return Ack(command,"invoked","已选择游戏确认窗口中的选项。");
        }
    }
}
