namespace PawnshopCompanion
{
    public sealed class ActionReceipt
    {
        public string action;
        public string status;
        public string summary;

        internal ActionReceipt(string actionName,string outcome,string text)
        {action=actionName;status=outcome;summary=text;}
    }

    // The game dispatcher resolves current object references and scope, then
    // invokes these vanilla actions on Unity's main thread.
    public static class NativeCoopActions
    {
        public static ActionReceipt SortStorage(GameGridInventory inventory,bool playerIsUsingTarget)
        {
            const string action="sort_storage";
            if(playerIsUsingTarget)return new ActionReceipt(action,"busy","你正在动这块，先等你放下。");
            if(inventory==null)return new ActionReceipt(action,"unavailable","这块仓库现在找不到了。");
            // Sort already applies vanilla ownership, locks and placement rules.
            bool applied=InventorySortHelper.Sort(inventory,true,false);
            return new ActionReceipt(action,applied?"applied":"blocked",
                applied?"仓库整理了一下。":"这块现在没法整理，东西先留在原处。");
        }

        public static ActionReceipt UndoStorageSort(GameGridInventory inventory,bool playerIsUsingTarget)
        {
            const string action="undo_storage_sort";
            if(playerIsUsingTarget)return new ActionReceipt(action,"busy","你正在动这块，先等你放下。");
            if(inventory==null)return new ActionReceipt(action,"unavailable","这块仓库现在找不到了。");
            bool applied=InventorySortHelper.Undo(inventory);
            return new ActionReceipt(action,applied?"applied":"blocked",
                applied?"刚才的整理撤回了。":"现在不能撤回那次整理了。");
        }

        public static ActionReceipt SwapBattery(GameItem installed,GameItem replacement,bool playerIsUsingTarget)
        {
            const string action="swap_battery";
            if(playerIsUsingTarget)return new ActionReceipt(action,"busy","你正在拿着相关物品，先等你放下。");
            if(installed==null || replacement==null || installed==replacement)
                return new ActionReceipt(action,"unavailable","眼前的电池位置变了，得重新看一眼。");
            if(!installed.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG) ||
               !replacement.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG))
                return new ActionReceipt(action,"blocked","这两个物品不能按电池交换。");
            if(!GeneralHelper.IsItemEffectivelyOwned(installed) || !GeneralHelper.IsItemEffectivelyOwned(replacement))
                return new ActionReceipt(action,"blocked","这不是店里可以动的电池。");
            GameInventory installedParent=installed.parentInventory,replacementParent=replacement.parentInventory;
            if(installedParent==null || replacementParent==null || installedParent==replacementParent)
                return new ActionReceipt(action,"blocked","电池不在可交换的位置。");
            if(PowerHelper.GetAvailableEnergyFromItem(replacement,false)<=0)
                return new ActionReceipt(action,"blocked","备用的这块也没电了。");
            if(!replacement.MayTarget(installed) || !replacement.CanTarget(installed))
                return new ActionReceipt(action,"blocked","这块备用电池现在换不进去。");
            // Follow the original item-on-item action, including its hooks.
            replacement.Target(installed);
            bool applied=replacement.parentInventory==installedParent && installed.parentInventory==replacementParent;
            return new ActionReceipt(action,applied?"applied":"blocked",
                applied?"电池换好了，旧电池也留着。":"这次没有换成，先重新看看实际位置。");
        }
    }
}
