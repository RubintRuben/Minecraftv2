using UnityEngine;

public class BlockOutlineMesh : MonoBehaviour
{
    public float inset = 0.0025f;
    public float thickness = 0.03f;
    public Color color = Color.black;

    private LineRenderer lrBottom;
    private LineRenderer lrTop;
    private LineRenderer[] lrVerts;

    private Material mat;

    private void Awake()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        lrBottom = CreateLR("Bottom");
        lrTop = CreateLR("Top");

        lrVerts = new LineRenderer[4];
        for (int i = 0; i < 4; i++)
            lrVerts[i] = CreateLR("V" + i);

        gameObject.SetActive(false);
    }

    private LineRenderer CreateLR(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = mat;
        lr.startWidth = thickness;
        lr.endWidth = thickness;
        lr.positionCount = 0;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    public void Show(Vector3Int blockPos)
    {
        Vector3 p0 = (Vector3)blockPos + new Vector3(inset, inset, inset);
        Vector3 p1 = (Vector3)blockPos + new Vector3(1f - inset, inset, inset);
        Vector3 p2 = (Vector3)blockPos + new Vector3(1f - inset, inset, 1f - inset);
        Vector3 p3 = (Vector3)blockPos + new Vector3(inset, inset, 1f - inset);

        Vector3 p4 = (Vector3)blockPos + new Vector3(inset, 1f - inset, inset);
        Vector3 p5 = (Vector3)blockPos + new Vector3(1f - inset, 1f - inset, inset);
        Vector3 p6 = (Vector3)blockPos + new Vector3(1f - inset, 1f - inset, 1f - inset);
        Vector3 p7 = (Vector3)blockPos + new Vector3(inset, 1f - inset, 1f - inset);

        lrBottom.startWidth = thickness;
        lrBottom.endWidth = thickness;
        lrTop.startWidth = thickness;
        lrTop.endWidth = thickness;
        for (int i = 0; i < 4; i++)
        {
            lrVerts[i].startWidth = thickness;
            lrVerts[i].endWidth = thickness;
        }

        lrBottom.positionCount = 5;
        lrBottom.SetPosition(0, p0);
        lrBottom.SetPosition(1, p1);
        lrBottom.SetPosition(2, p2);
        lrBottom.SetPosition(3, p3);
        lrBottom.SetPosition(4, p0);

        lrTop.positionCount = 5;
        lrTop.SetPosition(0, p4);
        lrTop.SetPosition(1, p5);
        lrTop.SetPosition(2, p6);
        lrTop.SetPosition(3, p7);
        lrTop.SetPosition(4, p4);

        lrVerts[0].positionCount = 2;
        lrVerts[0].SetPosition(0, p0);
        lrVerts[0].SetPosition(1, p4);

        lrVerts[1].positionCount = 2;
        lrVerts[1].SetPosition(0, p1);
        lrVerts[1].SetPosition(1, p5);

        lrVerts[2].positionCount = 2;
        lrVerts[2].SetPosition(0, p2);
        lrVerts[2].SetPosition(1, p6);

        lrVerts[3].positionCount = 2;
        lrVerts[3].SetPosition(0, p3);
        lrVerts[3].SetPosition(1, p7);

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }
}
