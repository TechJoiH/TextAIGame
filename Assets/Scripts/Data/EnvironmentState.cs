using System;
using UnityEngine;

namespace StateData.Environment
{
    [CreateAssetMenu(fileName = "EnvironmentData", menuName = "GameData/Environment")]
    public class EnvironmentData : ScriptableObject
    {
        public string locationId;
        public string locationName;     // 如：招摇山
        public string biome;            // 山地/沼泽/森林
        public WeatherType weather;
        public TimeOfDay timeOfDay;
        
        [TextArea(3, 6)]
        public string narrativeHint;    // 给 AI 的环境描写提示
    }

    public enum WeatherType { Clear, Foggy, Rainy, Stormy }
    public enum TimeOfDay { Dawn, Day, Dusk, Night }
}