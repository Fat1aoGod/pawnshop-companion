using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PawnshopCompanion
{
    public sealed partial class CompanionRuntime
    {
        private bool inventoryReadAllowed=true;
        private long inventoryRevision;

        private sealed class CatalogStep
        {
            internal GraphNodeStorage node;
            internal string parent;
            internal CatalogStep(GraphNodeStorage value,string parentRef){node=value;parent=parentRef;}
        }

        private IEnumerator InspectContainers(JObject command,Action<JObject> complete)
        {
            string id=(string)command["id"],startContext=context;
            JObject previous;
            if(executed.TryGetValue(id,out previous)){complete(previous);yield break;}
            JObject refusal=null;
            if(!active || (string)command["binding_id"]!=binding)refusal=Ack(command,"disconnected","陪玩已经停下了。");
            else if((string)command["context_id"]!=context)refusal=Ack(command,"stale_context","读档切换了，先重新看一眼。");
            else if((long?)command["expires_at"]<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())refusal=Ack(command,"expired","这次查看已经过时。");
            else if(!InStore())refusal=Ack(command,"unavailable","现在不在店里。");
            else if(!inventoryReadAllowed)refusal=Ack(command,"blocked","仓库和箱子还没有交给伙伴查看。");
            else if(Busy())refusal=Ack(command,"busy","你正在拿东西，等你放下再看。");
            if(refusal!=null){RememberCatalogReceipt(id,refusal);complete(refusal);yield break;}

            EmporiumEntry entry=EmporiumEntry.Instance;
            catalogObjects.Clear();itemOptions.Clear();machineControls.Clear();machineStores.Clear();
            string[] rootRefs={"storage-main","storage-back","counter-back","showcase","counter-front"};
            string[] rootNames={"店铺仓库","后屋仓库","后屋柜台","展示柜","前台柜台"};
            GameGridInventory[] inventories={entry.invElement,entry.backInvinvElement,entry.backInvinvElementCounter,entry.showcaseElement,entry.frontInvinvElement};
            string requested=(string)command["storage_ref"]??"all";
            JArray roots=new JArray(),items=new JArray();
            Stack<CatalogStep> pending=new Stack<CatalogStep>();
            HashSet<GraphNodeStorage> seen=new HashSet<GraphNodeStorage>();
            for(int n=inventories.Length-1;n>=0;n--)
            {
                if(inventories[n]==null || (requested!="all" && requested!=rootRefs[n]))continue;
                roots.Insert(0,new JObject{{"ref",rootRefs[n]},{"name",rootNames[n]}});
                pending.Push(new CatalogStep(inventories[n],rootRefs[n]));
            }
            if(roots.Count==0){refusal=Ack(command,"unavailable","这块仓库现在找不到了。");RememberCatalogReceipt(id,refusal);complete(refusal);yield break;}

            long revision=inventoryRevision;
            int visited=0,unread=0;
            float sliceStart=Time.realtimeSinceStartup;
            while(pending.Count>0 && visited<50000 && items.Count<12000)
            {
                if((visited&7)==0 && Time.realtimeSinceStartup-sliceStart>=0.002f)
                {
                    yield return null;
                    if(context!=startContext || !active || !inventoryReadAllowed || (string)command["binding_id"]!=binding)
                    {complete(Ack(command,"stale_context","查看途中换了场景或停下陪玩，这次结果没有继续使用。"));yield break;}
                    sliceStart=Time.realtimeSinceStartup;
                }
                CatalogStep step=pending.Pop();visited++;
                if(step.node==null || !seen.Add(step.node))continue;
                try
                {
                    GameItem item=step.node as GameItem;
                    if(item!=null)
                    {
                        if(!GeneralHelper.IsItemEffectivelyOwned(item))continue;
                        string reference="item-"+item.GetUniqueID();
                        catalogObjects[reference]=item;
                        string name=Plain(item.GetDisplayName());if(name.Length>120)name=name.Substring(0,120);
                        bool container=item.IsTag(ContainerHelper.CONTAINER_TAG) || item.IsTag(ContainerHelper.BACKPACK_TAG) || item.IsTag(ContainerHelper.POUCH_TAG) || item.IsTag(ContainerHelper.SAFE_TAG) || item.IsTag(ContainerHelper.PRE_CONTAINER_TAG);
                        PixelWindow content=item.contentWindow;
                        bool locked=GeneralHelper.IsItemLocked(item) || (content!=null && content.isLocked);
                        bool unopened=item.IsTag(ContainerHelper.PRE_CONTAINER_TAG) || (item.IsTag(ContainerHelper.TRACK_OPENED_TAG) && !item.IsTag(ContainerHelper.OPENED_TAG));
                        string access=locked?"locked":unopened?"sealed":content==null && container?"unavailable":"readable";
                        JObject record=new JObject{{"ref",reference},{"parent_ref",step.parent},{"name",name},{"quantity",item.unitCount},
                            {"is_container",container},{"has_contents",content!=null},{"access",access}};
                        GameGridInventory parent=item.parentInventory as GameGridInventory;
                        if(parent!=null)
                        {
                            record["grid_x"]=item.modifiedShape.minX;record["grid_y"]=item.modifiedShape.minY;
                        }
                        items.Add(record);
                        if(access!="readable"){unread++;continue;}
                        // Read the ordinary contents only. Do not walk examine windows,
                        // open UI, trigger first-opening rewards or generate sealed loot.
                        if(content!=null)pending.Push(new CatalogStep(content,reference));
                        continue;
                    }
                    List<GraphNodeStorage> children=step.node.children;
                    if(children!=null)for(int n=children.Count-1;n>=0;n--)pending.Push(new CatalogStep(children[n],step.parent));
                }
                catch(Exception){unread++;}
            }
            JObject result=Ack(command,"observed","看过仓库和箱子了，详细清单留在本机，找东西时直接查。");
            result["detail"]=new JObject{{"kind","container_catalog"},{"catalog_id",Guid.NewGuid().ToString("N")},
                {"context_id",startContext},{"inventory_revision",revision},{"roots",roots},{"items",items},
                {"truncated",pending.Count>0},{"unread_count",unread},{"changed_during_scan",revision!=inventoryRevision}};
            RememberCatalogReceipt(id,result);complete(result);
        }

        private void RememberCatalogReceipt(string id,JObject receipt)
        {
            executed[id]=receipt;executedOrder.Enqueue(id);
            while(executedOrder.Count>256)executed.Remove(executedOrder.Dequeue());
        }
    }
}
