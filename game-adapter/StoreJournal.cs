using System;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace PawnshopCompanion
{
    public sealed partial class CompanionRuntime
    {
        private JObject journalSummary;
        private int journalDay=-1;

        internal void MarkJournalDirty(){journalSummary=null;dirty=true;}
        private void JournalChanged(string unused){MarkJournalDirty();}

        private JObject CurrentJournalSummary()
        {
            int day=StoreStation.GetDayCounter();
            if(journalSummary!=null && journalDay==day)return journalSummary;
            PlayerStore store=PlayerStore.Instance;
            journalDay=day;
            journalSummary=new JObject{{"day",day},{"today",DayEvents(day)},
                {"rent",RentInfo(store,day)},{"traits",ActiveTraits(store,false)},
                {"reputation",Reputations(store,false)},{"difficulty",DifficultyInfo(false)},
                {"black_market",MarketInfo(false)}};
            return journalSummary;
        }
        private JArray DayEvents(int day)
        {
            JArray events=new JArray();
            StoreEventManager manager=StoreStation.Instance!=null?StoreStation.Instance.storeEventManager:null;
            if(manager==null)return events;
            // This is the calendar's own public filter, including announced future events.
            foreach(StoreEvent ev in manager.GetActiveEventOnDay(day,true,true))
            {
                if(ev==null || ev.isHidden)continue;
                string name=Plain(ev.GetDisplayName());if(name.Length==0)continue;
                events.Add(new JObject{{"name",name},{"effect",Plain(ev.GetEventPriceEffect())}});
            }
            return events;
        }
        private static JObject RentInfo(PlayerStore store,int day)
        {
            string kind=store.IsPropertyPaid?"owned":store.isPayingMortgage?"mortgage":"rent";
            return new JObject{{"kind",kind},{"days_until_due",kind=="owned"?(JToken)JValue.CreateNull():store.dayUntilRent},
                {"due_day",kind=="owned"?(JToken)JValue.CreateNull():day+store.dayUntilRent},
                {"amount",kind=="owned"?0:store.isPayingMortgage?store.mortgageWeeklyPayment:store.GetRentValue()}};
        }
        private static JArray ActiveTraits(PlayerStore store,bool detailed)
        {
            JArray traits=new JArray();
            if(store.startingPerks!=null)foreach(StartingPerk perk in store.startingPerks)
            {
                if(perk==null)continue;
                JObject item=new JObject{{"name",Plain(perk.GetLocalizedDisplayName())}};
                if(detailed)item["description"]=Plain(perk.GetLocalizedDescription());
                traits.Add(item);
            }
            return traits;
        }
        private static JArray Reputations(PlayerStore store,bool detailed)
        {
            JArray factions=new JArray();
            if(store.storeReputations!=null)foreach(StoreReputation faction in store.storeReputations)
            {
                JObject item=new JObject{{"name",Plain(faction.factionDisplay)},
                    {"value",Math.Round(faction.GetReputationExact(),2)},
                    {"status",Plain(StoreReputation.GetReputationStatus(faction.GetReputation()))}};
                if(detailed)
                {
                    JArray perks=new JArray();
                    if(faction.perks!=null)foreach(StoreReputationPerk perk in faction.perks)
                        perks.Add(new JObject{{"name",Plain(perk.GetLocalizedPerkName())},
                            {"threshold",perk.perkThreshold},{"active",perk.isUnlocked},
                            {"description",Plain(perk.GetLocalizedDescription())}});
                    item["perks"]=perks;
                }
                factions.Add(item);
            }
            return factions;
        }
        private static JObject DifficultyInfo(bool detailed)
        {
            Difficulty difficulty=Difficulty.Current;
            JObject info=new JObject{{"name",difficulty!=null?Plain(difficulty.GetTitle()):""},
                {"endless",PlayerStore.Instance.endlessMode}};
            if(detailed && difficulty!=null)
            {
                info["description"]=Plain(difficulty.GetSubtitle());
                JArray effects=new JArray();foreach(string line in Difficulty.GetEffectLines(difficulty))effects.Add(Plain(line));
                info["effects"]=effects;
            }
            return info;
        }
        private static JArray PanelText(GameObject root)
        {
            JArray lines=new JArray();if(root==null || !root.activeInHierarchy)return lines;
            foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if(!text.gameObject.activeInHierarchy)continue;
                string line=Plain(text.text);if(line.Length==0)continue;
                lines.Add(line);if(lines.Count>=100)break;
            }
            return lines;
        }
        private static JObject MarketInfo(bool detailed)
        {
            PlayerStore store=PlayerStore.Instance;
            bool unlocked=store.exchangeManager!=null && store.exchangeManager.isExchangeUnlocked;
            JObject market=new JObject{{"unlocked",unlocked}};
            if(detailed && unlocked)
            {
                ExchangeUIManager ui=ExchangeUIManager.Instance;
                bool open=ui!=null && ui.panel!=null && ui.panel.activeInHierarchy;
                market["panel_open"]=open;
                if(open)market["text"]=PanelText(ui.panel);
                else market["note"]="黑市已经解锁；打开黑市面板后可查看当前兑换项目。";
            }
            return market;
        }
        private JObject ReadJournal(JObject command)
        {
            string topic=(string)command["topic"]??"overview";
            PlayerStore store=PlayerStore.Instance;int day=StoreStation.GetDayCounter();
            JObject detail;
            if(topic=="overview") {MarkJournalDirty();detail=(JObject)CurrentJournalSummary().DeepClone();}
            else if(topic=="calendar")
            {
                int start=day-((day-1)%7);JArray days=new JArray();
                for(int current=start;current<start+14;current++)
                    days.Add(new JObject{{"day",current},{"today",current==day},{"events",DayEvents(current)}});
                detail=new JObject{{"day",day},{"days",days},{"rent",RentInfo(store,day)},
                    {"property_price",store.IsPropertyPaid?(JToken)JValue.CreateNull():store.GetStoreBuyPrice(false)}};
            }
            else if(topic=="status")detail=new JObject{{"traits",ActiveTraits(store,true)},
                {"cash",store.playerCash},{"estimated_total_assets",store.GetTotalEstimatedValue()}};
            else if(topic=="reputation")detail=new JObject{{"factions",Reputations(store,true)}};
            else if(topic=="difficulty")detail=DifficultyInfo(true);
            else if(topic=="black_market")detail=MarketInfo(true);
            else return Ack(command,"blocked","没有这个店铺信息页。");
            JObject result=Ack(command,"observed","翻了翻店铺的"+JournalTitle(topic)+"。");
            detail["topic"]=topic;result["detail"]=detail;return result;
        }
        private static string JournalTitle(string topic)
        {
            if(topic=="calendar")return "日历";if(topic=="status")return "天赋和状态";
            if(topic=="reputation")return "阵营声望";if(topic=="black_market")return "黑市";
            if(topic=="difficulty")return "难度";return "概况";
        }
        private JObject OpenJournal(JObject command)
        {
            string topic=(string)command["topic"];
            if(topic=="calendar" || topic=="status")
            {
                AdvCalendarUIManager ui=AdvCalendarUIManager.Instance;
                if(ui==null)return Ack(command,"unavailable","现在打不开日历。");
                ui.OpenUI();if(topic=="status")ui.OnStatusButtonClicked();
                if(ui.UIPanel==null || !ui.UIPanel.activeInHierarchy)return Ack(command,"unavailable","日历没有打开。");
            }
            else if(topic=="reputation")
            {
                ReputationUIManager ui=ReputationUIManager.Instance;
                if(ui==null)return Ack(command,"unavailable","现在打不开声望页。");
                ui.OpenUI();if(ui.uiPanel==null || !ui.uiPanel.activeInHierarchy)return Ack(command,"unavailable","声望页没有打开。");
            }
            else if(topic=="black_market")
            {
                if(PlayerStore.Instance.exchangeManager==null || !PlayerStore.Instance.exchangeManager.isExchangeUnlocked)
                    return Ack(command,"unavailable","这个存档的黑市还没有解锁。");
                ExchangeUIManager ui=ExchangeUIManager.Instance;
                if(ui==null)return Ack(command,"unavailable","现在打不开黑市。");
                ui.OpenPanel(null);if(ui.panel==null || !ui.panel.activeInHierarchy)return Ack(command,"unavailable","黑市没有打开。");
            }
            else return Ack(command,"blocked","没有这个可打开的信息页。");
            MarkJournalDirty();return Ack(command,"applied","打开了"+JournalTitle(topic)+"。");
        }
        private JObject VisibleJournalPanel()
        {
            AdvCalendarUIManager calendar=AdvCalendarUIManager.Instance;
            if(calendar!=null && calendar.UIPanel!=null && calendar.UIPanel.activeInHierarchy)
                return new JObject{{"visible",true},{"display_title",calendar.statusSection!=null && calendar.statusSection.activeSelf?"天赋和状态":"日历与夜间报告"},
                    {"displayed_text",String.Join("；",PanelText(calendar.UIPanel).ToObject<string[]>())}};
            ReputationUIManager reputation=ReputationUIManager.Instance;
            if(reputation!=null && reputation.uiPanel!=null && reputation.uiPanel.activeInHierarchy)
                return new JObject{{"visible",true},{"display_title","阵营声望"},{"displayed_text",String.Join("；",PanelText(reputation.uiPanel).ToObject<string[]>())}};
            ExchangeUIManager market=ExchangeUIManager.Instance;
            if(PlayerStore.Instance.exchangeManager!=null && PlayerStore.Instance.exchangeManager.isExchangeUnlocked && market!=null && market.panel!=null && market.panel.activeInHierarchy)
                return new JObject{{"visible",true},{"display_title","黑市"},{"displayed_text",String.Join("；",PanelText(market.panel).ToObject<string[]>())}};
            return null;
        }
        private string JournalPanelMarker()
        {
            AdvCalendarUIManager calendar=AdvCalendarUIManager.Instance;
            ReputationUIManager reputation=ReputationUIManager.Instance;ExchangeUIManager market=ExchangeUIManager.Instance;
            return "|"+(calendar!=null && calendar.UIPanel!=null && calendar.UIPanel.activeInHierarchy)+
                "|"+(calendar!=null && calendar.statusSection!=null && calendar.statusSection.activeInHierarchy)+
                "|"+(calendar!=null && calendar.nightlyReportSection!=null && calendar.nightlyReportSection.activeInHierarchy)+
                "|"+(reputation!=null && reputation.uiPanel!=null && reputation.uiPanel.activeInHierarchy)+
                "|"+(market!=null && market.panel!=null && market.panel.activeInHierarchy);
        }
    }
    [HarmonyPatch(typeof(StoreReputation),"ModReputation",new Type[]{typeof(double)})]
    internal static class ReputationChangedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StoreReputation),"ModReputationRaw",new Type[]{typeof(double)})]
    internal static class RawReputationChangedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StoreReputation),"SetReputation")]
    internal static class ReputationSetPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StoreEventManager),"ActivateEvent")]
    internal static class CalendarEventAddedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StoreEventManager),"DeactivateEvent")]
    internal static class CalendarEventRemovedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StoreEventManager),"QueueFuturEvent")]
    internal static class CalendarAnnouncementPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(AdvCalendarUIManager),"OpenUI")]
    internal static class JournalOpenedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
    [HarmonyPatch(typeof(StartingPerk),"RemovePerk")]
    internal static class TraitRemovedPatch {private static void Postfix(){if(CompanionRuntime.Current!=null)CompanionRuntime.Current.MarkJournalDirty();}}
}
