using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SecretManager
{
    [System.Serializable]
    public class KeyChain
    {
        public string gptApiKey;
        public string fallbackApiKey;
    }

    private static readonly Lazy<SecretManager> _instance =
        new Lazy<SecretManager>(() => new SecretManager());

    private KeyChain keyChain;

    private SecretManager()
    {
        var path = "Assets/Character/secrets.json";
        var json = File.ReadAllText(path);

        keyChain = JsonUtility.FromJson<KeyChain>(json);
    }

 
    public static SecretManager Instance
    {
        get
        {
            return _instance.Value;
        }
    }
    public string GetGPTSecrets()
    {
        return keyChain.gptApiKey;
    }

    public string GetFallbackAPIKey()
    {
        return keyChain.fallbackApiKey;
    }
}
