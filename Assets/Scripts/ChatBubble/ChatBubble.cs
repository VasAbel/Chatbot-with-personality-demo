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
        if (!faceCamera) return;

        if (!_cam) _cam = Camera.main;
        if (!_cam) return;

        // Vector from bubble to camera
        Vector3 toCam = (_cam.transform.position - transform.position).normalized;

        // Face the camera while keeping text upright using the camera's up vector
        transform.rotation = Quaternion.LookRotation(-toCam, _cam.transform.up);
    }

    public void Initialize(string content, float duration = -1f)
    {
        if (duration > 0f) lifetime = duration;

        var panelRt = (RectTransform)panel.transform;
        var textRt  = (RectTransform)text.transform;

        // Make sure pivots/anchors are in a sane state
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0f);
        panelRt.pivot     = new Vector2(0.5f, 0f);

        textRt.anchorMin = textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.pivot     = new Vector2(0.5f, 0.5f);

        text.enableWordWrapping = true;
        text.text = content;
        text.ForceMeshUpdate();

        // How wide may the TEXT itself be inside the bubble?
        float innerMaxWidth = maxWidth; // panel width will be inner + padding*2

        // Ask TMP what size it *wants* with wrapping
        Vector2 preferred = text.GetPreferredValues(content, innerMaxWidth, 0f);

        float textWidth  = Mathf.Min(preferred.x, innerMaxWidth);
        float textHeight = preferred.y;

        // Size the text box
        textRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        textRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   textHeight);

        // Size the panel around the text + padding
        float panelWidth  = textWidth  + padding.x * 2f;
        float panelHeight = textHeight + padding.y * 2f;

        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);
        panelRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   panelHeight);

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
