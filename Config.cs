using CounterStrikeSharp.API.Core;

namespace BeaconPlugin
{
    public class BeaconConfig : BasePluginConfig
    {
        public int PluginEnabled { get; set; } = 1;
        public string CommandAccess { get; set; } = "@css/generic";
        public int BeaconDuration { get; set; } = 60;
        public int EnableSound { get; set; } = 1;
        public string SoundPath { get; set; } = "sounds/tools/sfm/beep.vsnd_c";
    }
}