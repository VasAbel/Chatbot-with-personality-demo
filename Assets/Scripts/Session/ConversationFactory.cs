using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Creates concrete conversation-session instances.
/// It has no knowledge of active-session lifecycle, scene state, or ConversationManager.
/// </summary>
public static class ConversationFactory
{
    public static NPCConversationSession CreateNpcSession(NPC npc1, NPC npc2)
    {
        if (npc1 == null)
            throw new ArgumentNullException(nameof(npc1));

        if (npc2 == null)
            throw new ArgumentNullException(nameof(npc2));

        // Keep participant ordering deterministic so NPC-NPC conversation IDs and
        // first-speaker behavior remain stable regardless of which collider fired first.
        return npc1.idx <= npc2.idx
            ? new NPCConversationSession(npc1, npc2)
            : new NPCConversationSession(npc2, npc1);
    }

    public static UserConversationSession CreateUserSession(
        NPC npc,
        TMP_InputField dialogueBox,
        GameObject responseBox)
    {
        if (npc == null)
            throw new ArgumentNullException(nameof(npc));

        return new UserConversationSession(npc, dialogueBox, responseBox);
    }
}
