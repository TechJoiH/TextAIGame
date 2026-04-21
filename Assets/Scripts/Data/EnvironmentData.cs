using UnityEngine;

namespace StateData.Environment
{
    [CreateAssetMenu(fileName = "EnvironmentData", menuName = "GameData/Environment")]
    public class EnvironmentData : ScriptableObject
    {
        public string locationId;
        public string locationName;
        public string biome;
        public WeatherType weather;
        public TimeOfDay timeOfDay;

        [TextArea(3, 6)]
        public string narrativeHint;
    }
}
