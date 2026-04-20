using System;
using System.IO;
using UnityEngine;

public class SecretManager
{
    [Serializable]
    public class KeyChain
    {
        public string gptApiKey;
        public string awsAccessKey;
        public string awsSecretKey;
    }

    private static readonly Lazy<SecretManager> _instance =
        new Lazy<SecretManager>(() => new SecretManager());

    private KeyChain keyChain;

    private SecretManager()
    {
        var path = "Assets/Config Files/secrets.json";
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
}
