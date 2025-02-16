using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Assets.Game_Manager
{
    public class ConfigManager
    {
        [System.Serializable]
        public class Config
        {
            public int numPlayers;
            public int numConversationsInRound;
            public int timerLength;
            public string chatbotFallbackUrl;
        }

        [System.Serializable]
        public class CharacterDescriptions
        {
            public Description[] descriptions;
        }

        [System.Serializable]
        public class Description
        {
            public string name;
            public string description;
        }

        private static readonly Lazy<ConfigManager> _instance =
            new Lazy<ConfigManager>(() => new ConfigManager());

        private Config config;
        private CharacterDescriptions desc;

        private ConfigManager()
        {
            var c_path = "Assets/Character/config.json";
            var c_json = File.ReadAllText(c_path);
            config = JsonUtility.FromJson<Config>(c_json);

            var d_path = "Assets/Character/character_descriptions.json";
            var d_json = File.ReadAllText(d_path);
            desc = JsonUtility.FromJson<CharacterDescriptions>(d_json);
        }

        public static ConfigManager Instance => _instance.Value;

        public int GetNumPlayers() => config.numPlayers;
        public int GetNumConversationsInRound() => config.numConversationsInRound;
        public int GetTimerLength() => config.timerLength;
        public string GetChatbotFallbackUrl() => config.chatbotFallbackUrl;

        public string GetCharacterDescription(int idx) => desc.descriptions[idx].description;
        public Description GetFullCharacterDescription(int idx) => desc.descriptions[idx];
        public string GetCharacterName(int idx) => desc.descriptions[idx].name;
    }
}
