using System;
using UnityEngine;

namespace PawnshopCompanion
{
    public sealed partial class CompanionRuntime
    {
        private ConfigEntry<string> pairingSetting;
        private int bridgePort;
        private string bridgeToken="";
        private int pairingRevision;
        private void InitPairing()
        {
            pairingSetting=ModConfig.For("pawnshop_companion").Define("pairing_code","","本机连接",()=>"连接码",
                tooltip:()=>"运行接入包里的「首次连接」，将生成的连接码粘贴到这里，应用后进店开启陪玩。");
            ReadPairing(pairingSetting.Value);
            pairingSetting.OnChanged+=PairingChanged;
        }
        private static bool ParsePairing(string code,out int port,out string token)
        {
            port=0;token="";
            string[] parts=(code??"").Trim().Split(':');
            if(parts.Length!=3 || parts[0]!="PS1" || !Int32.TryParse(parts[1],out port) || port<1024 || port>65535 || parts[2].Length!=48)return false;
            foreach(char c in parts[2])if(!((c>='0' && c<='9') || (c>='a' && c<='f')))return false;
            token=parts[2];return true;
        }
        private bool ReadPairing(string code)
        {
            int port;string token;
            bool valid=ParsePairing(code,out port,out token);
            bridgePort=valid?port:0;bridgeToken=valid?token:"";pairingRevision++;
            return valid;
        }
        private void PairingChanged(string code)
        {
            active=false;disconnect=false;binding=null;partner=null;
            ResetContext();
            networkStatus=ReadPairing(code)?"连接码已保存，可以开始陪玩":"先在「显示」里填写本机连接码";
            RefreshStatus();
        }
        private void SavePairingFromPanel()
        {
            CustomUIElement field=petMenu.Get("pairing_code");
            if(field==null)return;
            string code=field.GetString().Trim();int port;string token;
            if(!ParsePairing(code,out port,out token)){ShowPetSpeech("提示","连接码不完整，请从首次连接窗口重新复制。");return;}
            pairingSetting.Set(code);
            networkStatus="连接码已保存，可以开始陪玩";RefreshStatus();
        }
    }
}
