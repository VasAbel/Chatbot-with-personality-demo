using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class OrthoAutoFramer : MonoBehaviour
{
    [Header("What to frame")]
    public LayerMask includeLayers;     // Environment + Characters
    public float paddingPercent = 0.01f; // 10% padding around bounds
    public float minSize = 5f;          // clamp ortho size
    public float maxSize = 200f;

    [Header("Camera pose")]
    [Range(15f, 70f)] public float tiltDegrees = 40f;
    [Range(0f, 360f)] public float yawDegrees = 225f;
    public float height = 50f;          // distance “up” along tilted view vector

    Camera cam;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        if (cam) cam.orthographic = true;
        FrameNow();
    }

    public void FrameNow()
    {
        if (!cam) cam = GetComponent<Camera>();
        if (!cam) return;

        // Gather renderers in the include layers
        var renderers = Object.FindObjectsOfType<Renderer>();
        Bounds? total = null;
        foreach (var r in renderers)
        {
            if (((1 << r.gameObject.layer) & includeLayers.value) != 0)
            {
                if (!total.HasValue) total = r.bounds;
                else total = Encapsulate(total.Value, r.bounds);
            }
        }
        if (!total.HasValue) return;

        var b = total.Value;

        // Set camera rotation (tilt + yaw)
        transform.rotation = Quaternion.Euler(tiltDegrees, yawDegrees, 0f);

        // Place camera so its forward looks at bounds center, from a fixed height along inverse forward
        var forward = transform.forward;
        var pos = b.center - forward * height;
        transform.position = pos;

        // Compute required ortho size to fit bounds in camera’s view
        // Project bounds extents onto camera right/up axes
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        float halfWidth = Mathf.Abs(Vector3.Dot(b.extents, right)) * 2f;
        float halfHeight = Mathf.Abs(Vector3.Dot(b.extents, up)) * 2f;

        float aspect = cam.aspect;
        // Ortho size is half of vertical size
        float sizeByHeight = (halfHeight * (1f + paddingPercent)) * 0.5f;
        float sizeByWidth = (halfWidth * (1f + paddingPercent)) * 0.5f / aspect;
        float targetSize = Mathf.Clamp(Mathf.Max(sizeByHeight, sizeByWidth), minSize, maxSize);

        targetSize *= 0.5f;
        cam.orthographicSize = targetSize;
    }

    Bounds Encapsulate(Bounds a, Bounds b)
    {
        a.Encapsulate(b.min);
        a.Encapsulate(b.max);
        return a;
    }
}

