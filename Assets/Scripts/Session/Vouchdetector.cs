using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// Every few seconds it reads the rumors the guard currently knows and asks the LLM whether each one counts asa vouch for the player.  
// When it finds one it calls GuardState.RegisterVouch() with the original sender's name.

public class VouchDetector : MonoBehaviour
{
    [Tooltip("How often (seconds) to check rumors for new vouches.")]
    public float checkInterval = 10f;
    private GuardState _guardState;
    private NPC _guardNpc;
    private GptClient _gpt;
    private readonly HashSet<string> _checkedRumorIds = new HashSet<string>();

    void Start()
    {
        _guardState = GetComponent<GuardState>();
        _guardNpc = GetComponent<NPC>();
        _gpt = FindObjectOfType<GptClient>();

        if (_guardState == null)
            Debug.LogError("[VouchDetector] GuardState not found on same GameObject.");

        StartCoroutine(CheckLoop());
    }

    private IEnumerator CheckLoop()
    {
        yield return new WaitForSeconds(5f);

        while (true)
        {
            yield return new WaitForSeconds(checkInterval);
            yield return CheckRumorsForVouches();
        }
    }

    private IEnumerator CheckRumorsForVouches()
    {
        if (RumorManager.Instance == null || _guardNpc == null || _gpt == null)
            yield break;

        var rumors = RumorManager.Instance.GetRumorsKnownBy(_guardNpc.getName());
        if (rumors == null || rumors.Count == 0) yield break;

        foreach (var rumor in rumors)
        {
            if (_checkedRumorIds.Contains(rumor.rumorId)) continue;
            _checkedRumorIds.Add(rumor.rumorId);

            bool done = false;
            _ = EvaluateRumorAsync(rumor, () => done = true);

            float waited = 0f;
            while (!done && waited < 8f)
            {
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }
        }
    }

    private async System.Threading.Tasks.Task EvaluateRumorAsync(Rumor rumor,
                                                                   System.Action onDone)
    {
        string system =
            "You are a classifier for a village simulation game. " +
            "Decide whether the following rumor constitutes a positive endorsement " +
            "of a stranger (the player character) by a villager — i.e. the villager " +
            "is vouching for the stranger's trustworthiness, good character, or that " +
            "they should be allowed somewhere or helped. " +
            "Reply with exactly one word: YES or NO.";

        string user =
            $"Rumor heard by the guard from {rumor.heardFrom}:\n" +
            $"\"{rumor.currentText}\"\n\n" +
            "Does this count as the villager vouching for the stranger?";

        try
        {
            string result = await _gpt.RequestGenericJsonAsync(system, user,
                                                               fallbackJson: "NO",
                                                               maxTokens: 5);
            result = result.Trim().Trim('"').ToUpperInvariant();

            if (result.StartsWith("YES"))
            {
                string voucher = rumor.heardFrom;
                if (voucher == "Player" && rumor.spreadChain.Count > 1)
                    voucher = rumor.spreadChain[1];

                if (_guardState.requiredVouchers.Contains(voucher))
                {
                    Debug.Log($"[VouchDetector] Rumor from {voucher} counts as a vouch: " +
                              $"\"{rumor.currentText}\"");
                    _guardState.RegisterVouch(voucher);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VouchDetector] LLM eval failed: {ex.Message}");
        }
        finally
        {
            onDone?.Invoke();
        }
    }
}