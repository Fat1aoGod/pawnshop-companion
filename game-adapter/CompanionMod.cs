using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace PawnshopCompanion
{
    public sealed class ModEntry : IMod
    {
        private GameObject root;
        public void Init(ModManifest manifest) { }
        public void OnEnable()
        {
            if(root!=null)return;
            root=new GameObject("PawnshopCompanion");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<CompanionRuntime>();
        }
        public void OnDisable()
        {
            if(root!=null)UnityEngine.Object.Destroy(root);
            root=null;
        }
    }

    public sealed partial class CompanionRuntime : MonoBehaviour
    {
        internal static CompanionRuntime Current;
        private readonly string gameId=Guid.NewGuid().ToString("N");
        private string context=Guid.NewGuid().ToString("N"),visit=Guid.NewGuid().ToString("N");
        private string binding,partner,networkStatus="尚未开启陪玩",marker="",lastStatus="";
        private string seenCustomerName="",seenCustomerLine="";
        private bool active,dirty=true,sortAllowed,powerAllowed,disconnect,wasCustomer,typing;
        private int storageIndex;
        private float nextProbe;
        private readonly List<JObject> events=new List<JObject>(),receipts=new List<JObject>();
        private readonly Dictionary<string,JObject> executed=new Dictionary<string,JObject>();
        private readonly Queue<string> executedOrder=new Queue<string>();
        private readonly Dictionary<string,GameItem> itemRefs=new Dictionary<string,GameItem>();
        private readonly Dictionary<string,GameInventory> expectedParents=new Dictionary<string,GameInventory>();
        private readonly List<string> chat=new List<string>();
        private CustomUIElement logElement,statusElement,inputElement;

        private void Awake()
        {
            Current=this;
            InitPairing();
            InitPetAssets();
            ModHook.OnGameLoadedLate+=ResetContext;
            ModHook.OnNewGameStarted+=NewGame;
            ModHook.OnShutterOpenedLate+=MarkDirty;
            ModHook.OnShutterClosedLate+=MarkDirty;
            ModHook.OnWakingUpLate+=MarkJournalDirty;
            ModHook.OnDifficultyApplied+=JournalChanged;
            ModHook.OnNetworkUpgradeUnlocked+=JournalChanged;
            SceneManager.sceneUnloaded+=SceneUnloaded;
            StartCoroutine(SyncLoop());
        }
        private void OnDestroy()
        {
            ModHook.OnGameLoadedLate-=ResetContext;
            ModHook.OnNewGameStarted-=NewGame;
            ModHook.OnShutterOpenedLate-=MarkDirty;
            ModHook.OnShutterClosedLate-=MarkDirty;
            ModHook.OnWakingUpLate-=MarkJournalDirty;
            ModHook.OnDifficultyApplied-=JournalChanged;
            ModHook.OnNetworkUpgradeUnlocked-=JournalChanged;
            SceneManager.sceneUnloaded-=SceneUnloaded;
            DisposePetAssets();
            ReleaseTyping();
            if(Current==this)Current=null;
        }
        private void NewGame(string unused){ResetContext();}
        private void SceneUnloaded(Scene unused){ResetContext();}
        private void ResetContext()
        {
            context=Guid.NewGuid().ToString("N");visit=Guid.NewGuid().ToString("N");
            dirty=true;marker="";wasCustomer=false;events.Clear();receipts.Clear();
            executed.Clear();executedOrder.Clear();itemRefs.Clear();expectedParents.Clear();
            ClearInteractions();
            DestroyPetOverlay();
            seenCustomerName="";seenCustomerLine="";
            journalSummary=null;journalDay=-1;
        }
        internal void MarkDirty(){dirty=true;}
        internal void MarkInventoryDirty(){inventoryRevision++;dirty=true;}
        internal void CustomerArrived(){visit=Guid.NewGuid().ToString("N");wasCustomer=true;seenCustomerName="";seenCustomerLine="";dirty=true;}
        internal void CustomerLeft(){wasCustomer=false;seenCustomerName="";seenCustomerLine="";dirty=true;}
        private bool InStore(){return EmporiumEntry.Instance!=null && PlayerStore.Instance!=null && !ModHelper.IsOutsideStore();}
        private void Update()
        {
            UpdatePetOverlay();
            if(Input.GetKeyDown(KeyCode.F10) && InStore() && !StoreUIManager.IsAnyTyping())ToggleWindow();
            if(!active || Time.unscaledTime<nextProbe || !InStore())return;
            nextProbe=Time.unscaledTime+0.75f;
            PlayerStore store=PlayerStore.Instance;
            if(store.isClientArrived && !wasCustomer)CustomerArrived();
            CaptureCustomerText();
            string date=DateLabel();
            string next=store.playerCash+"|"+store.isClientArrived+"|"+store.isPowerOn+"|"+date+"|"+seenCustomerName+"|"+seenCustomerLine+JournalPanelMarker();
            if(next!=marker){marker=next;dirty=true;}
            wasCustomer=store.isClientArrived;
        }
        private void CaptureCustomerText()
        {
            if(PlayerStore.Instance==null || !PlayerStore.Instance.isClientArrived)return;
            DialogUIManager dialog=DialogUIManager.Instance;if(dialog==null)return;
            if(dialog.clientNameBubble!=null && dialog.clientNameBubble.activeInHierarchy && dialog.clientNameTMP!=null)
                seenCustomerName=Plain(dialog.clientNameTMP.text);
            if(Visible(dialog.clientBubble))seenCustomerLine=Plain(dialog.clientBubble.textfield.text);
        }
        private static bool Visible(TextBubble bubble){return bubble!=null && bubble.textfield!=null && bubble.gameObject.activeInHierarchy;}
        private static string Plain(string text)
        {
            if(String.IsNullOrEmpty(text))return "";
            StringBuilder value=new StringBuilder();bool tag=false;
            foreach(char ch in text){if(ch=='<'){tag=true;continue;}if(ch=='>'){tag=false;continue;}if(!tag)value.Append(ch);}
            return value.ToString().Trim();
        }
        private static string DateLabel()
        {
            StoreUIManager ui=StoreUIManager.Instance;
            return ui!=null && ui.storeCalendar!=null && ui.storeCalendar.dayTMP!=null?Plain(ui.storeCalendar.dayTMP.text):"";
        }
        private GameGridInventory Storage()
        {
            EmporiumEntry entry=EmporiumEntry.Instance;if(entry==null)return null;
            return storageIndex==1?entry.backInvinvElement:entry.invElement;
        }
        private string StorageName(){return storageIndex==1?"后屋仓库":"店铺仓库";}
        private string StorageRef(){return storageIndex==1?"storage-back":"storage-main";}
        private JObject Scope()
        {
            return new JObject { {"sort_storage",sortAllowed},{"power",powerAllowed},{"inspect_containers",inventoryReadAllowed},
                {"inventory_revision",inventoryRevision},
                {"interact",interactionsAllowed},
                {"storage_ref",StorageRef()},{"storage_name",StorageName() }};
        }
        private JObject Snapshot()
        {
            if(!InStore())return new JObject{{"context_id",context},{"place","暂时离开店铺"},{"player_name","玩家"}};
            PlayerStore store=PlayerStore.Instance;EmporiumEntry entry=EmporiumEntry.Instance;
            JObject state=new JObject{{"context_id",context},{"place","店里"},{"day_label",DateLabel()},
                {"player_name",store.playerName},{"displayed_cash",store.playerCash}};
            state["surroundings"]=new JArray(new JObject{{"visible",true},{"text",store.isPowerOn?"店铺的电源开着。":"店铺的电源关着。"}});
            DialogUIManager dialog=DialogUIManager.Instance;
            CaptureCustomerText();
            if(store.isClientArrived)
                state["customer"]=new JObject{{"visible",true},{"visit_id",visit},
                    {"display_name",seenCustomerName},{"displayed_dialogue",seenCustomerLine}};
            JArray counter=new JArray();
            if(entry.frontInvWindow!=null && entry.frontInvWindow.IsVisible() && entry.frontInvinvElement!=null)
                foreach(GameItem item in entry.frontInvinvElement.childItems)
                    if(counter.Count<12)counter.Add(new JObject{{"visible",true},{"display_name",Plain(item.GetDisplayName())},{"display_quantity",item.unitCount}});
            state["counter"]=counter;
            JArray choices=new JArray();
            if(dialog!=null)foreach(DialogChoiceBubble choice in dialog.dialogChoiceBubbles)
                if(choice!=null && choice.TMP!=null && choice.gameObject.activeInHierarchy && choice.IsInteractable())
                    choices.Add(new JObject{{"visible",true},{"enabled",true},{"label",Plain(choice.TMP.text)}});
            state["actions"]=choices;
            state["journal"]=CurrentJournalSummary();
            state["panel"]=VisibleJournalPanel();
            return state;
        }
        private void AddChat(string who,string text)
        {
            chat.Add(who+"："+text);
            while(chat.Count>12)chat.RemoveAt(0);
            if(logElement!=null && logElement.IsAlive)logElement.SetText(String.Join("\n\n",chat.ToArray()));
            ShowPetSpeech(who,text);
        }
        private void RefreshStatus()
        {
            string text=partner!=null?partner+"在店里 · F10 收起":networkStatus+" · F10 收起";
            if(text!=lastStatus && statusElement!=null && statusElement.IsAlive){statusElement.SetText(text);lastStatus=text;}
            RefreshPetStatus();
            RefreshPanelStatus();
        }
        private void ReleaseTyping(){if(typing && StoreUIManager.Instance!=null)StoreUIManager.Instance.StopTyping();typing=false;}
        private void ToggleWindow(){TogglePetMenu();}
        private void SendPlayer()
        {
            if(inputElement==null || !inputElement.IsAlive)return;
            string text=inputElement.GetString().Trim();if(text.Length==0)return;
            if(!active)StartCompanionship();
            if(!active)return;
            if(events.Count>=32){AddChat("提示","本机连接还没接通，稍等一下再发。");return;}
            events.Add(new JObject{{"id",Guid.NewGuid().ToString("N")},{"type","player_message"},{"text",text}});
            AddChat(PlayerStore.Instance!=null?PlayerStore.Instance.playerName:"你",text);
            inputElement.SetString("",false);dirty=true;
            ReleaseTyping();
        }
        private IEnumerator SyncLoop()
        {
            while(true)
            {
                if(!active && !disconnect){yield return new WaitForSecondsRealtime(0.75f);continue;}
                if(!InStore() && !disconnect){yield return new WaitForSecondsRealtime(1);continue;}
                if(bridgeToken.Length==0){yield return new WaitForSecondsRealtime(1);continue;}
                string sentContext=context;
                int sentPairing=pairingRevision;
                JObject payload=new JObject{{"protocol_version","0.1"},{"game_instance_id",gameId},{"context_id",context},
                    {"active",active},{"disconnect",disconnect},{"scope",Scope()},
                    {"events",new JArray(events)},{"receipts",new JArray(receipts)}};
                if(dirty)payload["snapshot"]=Snapshot();
                int eventCount=events.Count,receiptCount=receipts.Count;
                using(UnityWebRequest request=new UnityWebRequest("http://127.0.0.1:"+bridgePort+"/game/sync","POST"))
                {
                    request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)));
                    request.downloadHandler=new DownloadHandlerBuffer();request.timeout=3;
                    request.SetRequestHeader("Content-Type","application/json");request.SetRequestHeader("Authorization","Bearer "+bridgeToken);
                    dirty=false;
                    yield return request.SendWebRequest();
                    if(context!=sentContext || sentPairing!=pairingRevision){dirty=true;continue;}
                    if(request.result!=UnityWebRequest.Result.Success)
                    {
                        dirty=true;networkStatus="本机伙伴还没接通，正在等连接";RefreshStatus();
                        yield return new WaitForSecondsRealtime(3);continue;
                    }
                    JObject response=null;
                    try{response=JObject.Parse(request.downloadHandler.text);}catch(Exception){dirty=true;networkStatus="连接返回异常，稍后重试";}
                    if(response!=null)
                    {
                        for(int n=0;n<Math.Min(receiptCount,receipts.Count);n++)
                            if(receipts[n]["detail"]!=null && (string)receipts[n]["detail"]["kind"]=="container_catalog")receipts[n].Remove("detail");
                        events.RemoveRange(0,Math.Min(eventCount,events.Count));receipts.RemoveRange(0,Math.Min(receiptCount,receipts.Count));disconnect=false;
                        binding=(string)response["binding_id"];partner=(string)response["companion_name"];
                        if(!manualAvatar)SetPetPortrait((string)response["companion_avatar"]);
                        networkStatus=active?"本机已接通，等伙伴来店里":"陪玩已停止";RefreshStatus();
                        JArray commands=response["commands"] as JArray;
                        if(commands!=null)foreach(JObject command in commands)
                        {
                            if((string)command["action"]=="inspect_containers")
                            {
                                yield return InspectContainers(command,result=>receipts.Add(result));
                                continue;
                            }
                            JObject receipt;
                            try{receipt=Execute(command);}catch(Exception error){receipt=Ack(command,"blocked","这次操作没有完成："+error.GetType().Name);}
                            receipts.Add(receipt);
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }
        private bool Busy()
        {
            EmporiumEntry entry=EmporiumEntry.Instance;
            return Input.GetMouseButton(0) || Input.GetMouseButton(1) || UIDragHandler.isDraggingAnyUI ||
                (ItemMouseDragHandler.current!=null && ItemMouseDragHandler.current.IsDraggingItem) ||
                (ItemMultiSelectHandler.current!=null && (ItemMultiSelectHandler.current.IsGroupDragging || ItemMultiSelectHandler.current.HasSelection)) ||
                (entry!=null && entry.swapBufferElement!=null && entry.swapBufferElement.childItem!=null) ||
                PlayerStore.Instance.lockAllInventoryFlag>0;
        }
        private JObject Ack(JObject command,string status,string summary)
        {
            return new JObject{{"command_id",(string)command["id"]},{"status",status},{"summary",summary}};
        }
        private JObject Execute(JObject command)
        {
            string id=(string)command["id"],action=(string)command["action"];
            JObject previous;if(executed.TryGetValue(id,out previous))return previous;
            JObject result;
            if(!active || (string)command["binding_id"]!=binding)result=Ack(command,"disconnected","陪玩已经停下了。");
            else if((string)command["context_id"]!=context)result=Ack(command,"stale_context","读档已经切换，这次没有执行。");
            else if((long?)command["expires_at"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())result=Ack(command,"expired","这条操作已经过时，这次没有执行。");
            else if(!InStore())result=Ack(command,"unavailable","现在不在店里。");
            else if(action=="say")
            {
                string targetVisit=(string)command["visit_id"];
                if(targetVisit!=null && (targetVisit!=visit || !PlayerStore.Instance.isClientArrived))result=Ack(command,"stale_visit","那位顾客已经离开，这条话没有插到下一位顾客那里。");
                else {AddChat(partner??"伙伴",(string)command["text"]);bool shown=(petSpeech!=null && petSpeech.gameObject.activeInHierarchy)||(petMenu!=null && petMenu.IsAlive && petMenu.IsVisible);result=Ack(command,shown?"displayed":"received","消息已送到游戏伙伴身边。");}
            }
            else if(action=="read_journal")result=ReadJournal(command);
            else if(Busy())result=Ack(command,"busy","你正在操作，先等你放下。");
            else if(action=="open_journal")result=interactionsAllowed?OpenJournal(command):Ack(command,"blocked","界面还没有交给伙伴操作。");
            else if(action=="item_options")result=inventoryReadAllowed?InspectItemOptions(command):Ack(command,"blocked","物品还没有交给伙伴查看。");
            else if(action=="machine_controls")result=inventoryReadAllowed?InspectMachineControls(command):Ack(command,"blocked","机器还没有交给伙伴查看。");
            else if(action=="read_confirmation")
            {result=Ack(command,"observed","当前的游戏确认窗口。");result["detail"]=new JObject{{"confirmation",CurrentConfirmation()}};}
            else if(action=="item_action" || action=="machine_control" || action=="move_item" || action=="use_item_on" || action=="answer_confirmation")
            {
                if(!interactionsAllowed)result=Ack(command,"blocked","物品和机器还没有交给伙伴操作。");
                else if(action=="item_action")result=ApplyItemOption(command);
                else if(action=="machine_control")result=ApplyMachineControl(command);
                else if(action=="move_item")result=MoveNativeItem(command);
                else if(action=="use_item_on")result=UseNativeItemOn(command);
                else result=AnswerConfirmation(command);
            }
            else if(action=="sort_storage" || action=="undo_storage_sort" || action=="inspect_storage")
            {
                if(!(action=="inspect_storage"?inventoryReadAllowed:sortAllowed) || (string)command["storage_ref"]!=StorageRef())result=Ack(command,"blocked","这块仓库还没有交给伙伴。");
                else if(action=="inspect_storage")result=InspectStorage(command);
                else
                {
                    ActionReceipt receipt=action=="sort_storage"?NativeCoopActions.SortStorage(Storage(),false):NativeCoopActions.UndoStorageSort(Storage(),false);
                    result=Ack(command,receipt.status,receipt.summary);if(receipt.status=="applied")MarkInventoryDirty();
                }
            }
            else if(action=="inspect_power")result=powerAllowed?InspectPower(command):Ack(command,"blocked","电池和机器还没有交给伙伴。");
            else if(action=="swap_battery")
            {
                GameItem installed,replacement;string first=(string)command["installed_ref"],second=(string)command["replacement_ref"];
                if(!powerAllowed)result=Ack(command,"blocked","电池还没有交给伙伴。");
                else if(first==null || second==null || !itemRefs.TryGetValue(first,out installed) || !itemRefs.TryGetValue(second,out replacement) ||
                    installed.parentInventory!=expectedParents[first] || replacement.parentInventory!=expectedParents[second])
                    result=Ack(command,"unavailable","电池位置变了，先重新看一眼。");
                else
                {
                    ActionReceipt receipt=NativeCoopActions.SwapBattery(installed,replacement,false);
                    result=Ack(command,receipt.status,receipt.summary);if(receipt.status=="applied")MarkInventoryDirty();itemRefs.Clear();expectedParents.Clear();
                }
            }
            else result=Ack(command,"blocked","没有开放这个操作。");
            executed[id]=result;executedOrder.Enqueue(id);
            while(executedOrder.Count>256)executed.Remove(executedOrder.Dequeue());
            if(action!="say" && action!="inspect_power" && action!="inspect_storage" && action!="item_options" && action!="machine_controls" && action!="read_confirmation" && action!="read_journal")AddChat(partner??"伙伴",(string)result["summary"]);
            return result;
        }
        private JObject InspectStorage(JObject command)
        {
            GameGridInventory storage=Storage();if(storage==null)return Ack(command,"unavailable","这块仓库没打开。");
            JArray list=new JArray();foreach(GameItem item in storage.childItems)
            {
                if(list.Count>=120)break;
                list.Add(new JObject{{"name",Plain(item.GetDisplayName())},{"quantity",item.unitCount}});
            }
            JObject result=Ack(command,"observed",StorageName()+"里有 "+storage.childItems.Count+" 件物品。");
            result["detail"]=new JObject{{"storage_ref",StorageRef()},{"items",list}};return result;
        }
        private JObject InspectPower(JObject command)
        {
            itemRefs.Clear();expectedParents.Clear();
            JArray batteries=new JArray(),machines=new JArray();
            HashSet<GraphNodeStorage> seen=new HashSet<GraphNodeStorage>();
            Stack<GraphNodeStorage> stack=new Stack<GraphNodeStorage>();
            if(Storage()!=null)stack.Push(Storage());
            int visited=0;
            while(stack.Count>0 && visited++<1500)
            {
                GraphNodeStorage node=stack.Pop();if(node==null || !seen.Add(node))continue;
                GameItem item=node as GameItem;
                if(item!=null && !GeneralHelper.IsItemEffectivelyOwned(item))continue;
                if(item!=null && item.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG))
                {
                    string reference="battery-"+item.GetUniqueID();itemRefs[reference]=item;expectedParents[reference]=item.parentInventory;
                    GameItem holder=item.parentInventory!=null?item.parentInventory.GetParentItem():null;
                    batteries.Add(new JObject{{"ref",reference},{"name",Plain(item.GetDisplayName())},
                        {"location",holder!=null?Plain(holder.GetDisplayName()):StorageName()},
                        {"energy",PowerHelper.GetAvailableEnergyFromItem(item,false)},
                        {"capacity",item.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt()}});
                }
                if(item!=null && (item.IsTag(MachineryHelper.MACHINERY_TAG) || item.IsTag(MachineHelper.STANDARD_MACHINE_TAG)))
                {
                    GameSlotInventory slot=MachineHelper.GetBatterySlot(item);
                    GameItem battery=slot!=null?slot.childItem:null;
                    machines.Add(new JObject{{"name",Plain(item.GetDisplayName())},
                        {"battery_ref",battery!=null?"battery-"+battery.GetUniqueID():null},
                        {"has_battery_slot",slot!=null},
                        {"battery_can_supply_next_cycle",slot!=null && MachineHelper.CanPower(item,slot)},
                        {"required_power",MachineryHelper.GetMachinePowerUsage(item)}});
                }
                foreach(GraphNodeStorage child in node.children)if(child!=null)stack.Push(child);
            }
            JObject result=Ack(command,"observed","看了看"+StorageName()+"和里面设备的供电情况。");
            result["detail"]=new JObject{{"storage_ref",StorageRef()},{"batteries",batteries},{"machines",machines},{"truncated",stack.Count>0}};return result;
        }
    }

    [HarmonyPatch(typeof(StoreClientMono),"OnArrived")]
    internal static class ArrivedPatch { private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.CustomerArrived();} }
    [HarmonyPatch(typeof(StoreClientMono),"OnLeft")]
    internal static class LeftPatch { private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.CustomerLeft();} }
    [HarmonyPatch(typeof(DialogUIManager),"DisplayClientText")]
    internal static class DialoguePatch { private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkDirty();} }
    [HarmonyPatch(typeof(PlayerStore),"OnMainInvAreaChange")]
    internal static class InventoryPatch { private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkInventoryDirty();} }
}
