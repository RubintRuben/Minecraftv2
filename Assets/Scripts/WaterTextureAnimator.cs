using UnityEngine;

public class WaterTextureAnimator : MonoBehaviour
{
    public Material waterMaterial;
    public int frameSize = 16;
    public int frames = 32;
    public float fps = 8f;

    float t;

    void Start()
    {
        if (waterMaterial == null) return;
        if (frames < 1) frames = 1;
        waterMaterial.mainTextureScale = new Vector2(1f, 1f / frames);
    }

    void Update()
    {
        if (waterMaterial == null) return;
        if (frames < 1) frames = 1;
        t += Time.deltaTime * Mathf.Max(0.01f, fps);
        int i = Mathf.FloorToInt(t) % frames;
        float v = 1f - (i + 1) / (float)frames;
        waterMaterial.mainTextureOffset = new Vector2(0f, v);
    }
}
