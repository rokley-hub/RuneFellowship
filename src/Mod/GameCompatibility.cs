using System;
using System.Linq;
using HarmonyLib;
namespace Rune.Mod
{
    internal static class GameCompatibility
    {
        internal static void AssignCreator(this Piece piece,long owner)
        {
            var method=typeof(Piece).GetMethods().First(m=>m.Name=="SetCreator");
            if(method.GetParameters().Length==1) {method.Invoke(piece,new object[]{owner});return;}
            // Match Player.PlacePiece: retain both character and platform ownership.
            var manager=AccessTools.TypeByName("Splatform.PlatformManager");
            var platform=AccessTools.Property(manager,"DistributionPlatform").GetValue(null);
            var local=AccessTools.Property(platform.GetType(),"LocalUser").GetValue(platform);
            var userId=AccessTools.Property(local.GetType(),"PlatformUserID").GetValue(local);
            method.Invoke(piece,new[]{(object)owner,userId});
        }
        internal static string WorldSavePath()
        {
            var old=AccessTools.Method(typeof(World),"GetWorldSavePath",new[]{typeof(FileHelpers.FileSource)});
            if(old!=null)return (string)old.Invoke(null,new object[]{FileHelpers.FileSource.Local});
            var type=AccessTools.TypeByName("SaveSystem");
            var method=type.GetMethods().First(m=>m.Name=="GetSavePath"&&m.GetParameters().Length==2);
            var kind=Enum.Parse(method.GetParameters()[0].ParameterType,"World");
            return (string)method.Invoke(null,new[]{kind,(object)FileHelpers.FileSource.Local});
        }
        internal static string CharacterSavePath()
        {
            var type=AccessTools.Method(typeof(PlayerProfile),"GetCharacterFolderPath",new[]{typeof(FileHelpers.FileSource)}) ?? AccessTools.Method(AccessTools.TypeByName("SaveSystem"),"GetCharacterFolderPath",new[]{typeof(FileHelpers.FileSource)});
            return (string)type.Invoke(null,new object[]{FileHelpers.FileSource.Local});
        }
    }
}
