using System.Collections.Generic;
using UnityEngine;

public class GuardState : MonoBehaviour
{
    [Header("Unlock Settings")]
    [Tooltip("Trust needed when ALL villagers have vouched (0–100).")]
    public float unlockedByVouchThreshold = 60f;

    [Tooltip("Names of villagers whose vouch counts. Must match NPC names exactly.")]
    public List<string> requiredVouchers = new List<string> { "Tim", "Amy", "Gabriel" };

    [Header("Scene References")]
    [Tooltip("The door GameObject to deactivate when unlocked.")]
    public GameObject door;

    [Tooltip("Optional: a visual indicator shown when the door is unlocked.")]
    public GameObject unlockedFeedback;

    public float TrustLevel { get; private set; } = 0f;
    public bool IsDoorUnlocked { get; private set; } = false;

    private readonly HashSet<string> _vouches = new HashSet<string>();

 
    // Called by ConsoleChatbot after each player-Guard exchange,
    public void ApplyTrustDelta(float delta)
    {
        TrustLevel = Mathf.Clamp(TrustLevel + delta, 0f, 100f);
        Debug.Log($"[GuardState] TrustLevel → {TrustLevel:F1}  (delta {delta:+0;-0})");
        TryUnlock();
    }

    //Each villager name may only vouch once.
    public void RegisterVouch(string villagerName)
    {
        if (_vouches.Contains(villagerName)) return;

        _vouches.Add(villagerName);
        Debug.Log($"[GuardState] Vouch received from {villagerName}. " +
                  $"({_vouches.Count}/{requiredVouchers.Count} required)");
        TryUnlock();
    }

    public int VouchCount => _vouches.Count;
    public int RequiredVouchCount => requiredVouchers.Count;
    public bool HasVouch(string name) => _vouches.Contains(name);

    private void TryUnlock()
    {
        if (IsDoorUnlocked) return;

        bool allVouched = requiredVouchers.TrueForAll(n => _vouches.Contains(n));
        bool trustOk = TrustLevel >= unlockedByVouchThreshold;

        if (allVouched && trustOk)
            Unlock();
    }

    private void Unlock()
    {
        IsDoorUnlocked = true;

        if (door != null)
            door.SetActive(false);

        if (unlockedFeedback != null)
            unlockedFeedback.SetActive(true);

        Debug.Log("[GuardState] All conditions met — door is now open!");
    }
}