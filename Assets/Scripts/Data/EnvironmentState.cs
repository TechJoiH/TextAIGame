using System;
using UnityEngine;

namespace StateData.Environment
{
    [CreateAssetMenu(fileName = "EnvironmentData", menuName = "GameData/Environment")]
    public class EnvironmentData : ScriptableObject
    {
        public string locationId;
        public string locationName;     // 如：危摇山
        public string biome;            // 山脉/沼泽/森林
        public WeatherType weather;
        public TimeOfDay timeOfDay;
        
        [TextArea(3, 6)]
        public string narrativeHint;    // 给 AI 的环境续写提示
    }

    public enum WeatherType { Clear, Foggy, Rainy, Stormy }
    public enum TimeOfDay { Dawn, Day, Dusk, Night }

    /// <summary>
    /// 可序列化的环境运行时状态（用于 JSON 注入 AI Prompt）
    /// </summary>
    [Serializable]
    public sealed class EnvironmentState
    {
        public string locationId;
        public string locationName;
        public string biome;
        public string weather;      // 字符串化便于 AI 理解
        public string timeOfDay;
        public string narrativeHint;

        // 环境标签（用于本地逻辑判定）
        public bool isWet;          // 潮湿环境（雨天/沼泽）
        public bool isDark;         // 黑暗环境（夜晚/洞穴）
        public bool isWindy;        // 大风环境（暴风雨）
        public bool isFoggy;        // 迷雾环境

        /// <summary>
        /// 从 ScriptableObject 构建运行时状态
        /// </summary>
        public static EnvironmentState FromData(EnvironmentData data)
        {
            if (data == null) return GetDefault();

            var state = new EnvironmentState
            {
                locationId = data.locationId,
                locationName = data.locationName,
                biome = data.biome,
                weather = data.weather.ToString(),
                timeOfDay = data.timeOfDay.ToString(),
                narrativeHint = data.narrativeHint,
                
                // 推导环境标签
                isWet = data.weather == WeatherType.Rainy || data.weather == WeatherType.Stormy || data.biome == "沼泽",
                isDark = data.timeOfDay == TimeOfDay.Night || data.timeOfDay == TimeOfDay.Dusk,
                isWindy = data.weather == WeatherType.Stormy,
                isFoggy = data.weather == WeatherType.Foggy
            };
            return state;
        }

        public static EnvironmentState GetDefault()
        {
            return new EnvironmentState
            {
                locationId = "unknown",
                locationName = "未知之地",
                biome = "荒野",
                weather = "Clear",
                timeOfDay = "Day",
                narrativeHint = "",
                isWet = false,
                isDark = false,
                isWindy = false,
                isFoggy = false
            };
        }
    }
}