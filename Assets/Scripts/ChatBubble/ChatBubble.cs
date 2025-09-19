using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class ChatBubble : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private TMP_Text text;
    [SerializeField] private Graphic panel; // background image/panel

    [Header("Behavior")]
    [SerializeField] private float lifetime = 3.0f;
    [SerializeField] private float fadeDuration = 0.25f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Vector2 padding = new Vector2(16, 12);
    [SerializeField] private float maxWidth = 420f; // in canvas units (pixels in world-space canvas)

    private Camera _cam;
    private float _t;

    private void Awake()
    {
        if (!canvas) canvas = GetComponentInChildren<Canvas>(true);
        if (!text) text = GetComponentInChildren<TMP_Text>(true);
        if (!panel) panel = GetComponentInChildren<Graphic>(true);
        _cam = Camera.main;
    }

    private void LateUpdate()
    {
        if (faceCamera && _cam)
        {
            // billboard
            transform.forward = _cam.transform.forward;
        }
    }

    public void Initialize(string content, float duration = -1f)
    {
        if (duration > 0f) lifetime = duration;

        text.enableWordWrapping = true;
        text.text = content;
        text.ForceMeshUpdate();

        // Clamp width by enabling TMP auto-sizing and letting layout settle
        var rt = text.GetComponent<RectTransform>();
        var panelRt = panel.GetComponent<RectTransform>();

        // Set a preferred width cap
        var preferred = text.GetPreferredValues(content, maxWidth, 0f);
        float w = Mathf.Min(preferred.x + padding.x * 2f, maxWidth + padding.x * 2f);
        float h = text.preferredHeight + padding.y * 2f;

        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

        // start lifecycle
        StopAllCoroutines();
        StartCoroutine(LifeCoroutine());
    }

    private IEnumerator LifeCoroutine()
    {
        // fully visible
        SetAlpha(1f);

        // wait
        yield return new WaitForSeconds(lifetime);

        // fade out
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            SetAlpha(a);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetAlpha(float a)
    {
        if (panel) panel.canvasRenderer.SetAlpha(a);
        if (text) text.canvasRenderer.SetAlpha(a);
    }
}
