using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI World Space sobre el objeto "News" del Lobby: resultado + dinero.
/// Orientado según la normal del plano (Mesh / Plane).
/// </summary>
public class NewsBoardPresenter : MonoBehaviour
{
    [SerializeField]
    string newsObjectName = "News";

    [SerializeField]
    Vector2 canvasSize = new Vector2(900f, 1100f);

    [SerializeField]
    float canvasScale = 0.005f;

    [SerializeField]
    float surfaceOffset = 0.02f;

    Text bodyText;
    Transform newsRoot;
    bool built;

    public static void RefreshAll()
    {
        NewsBoardPresenter[] boards =
            Object.FindObjectsByType<NewsBoardPresenter>(FindObjectsSortMode.None);
        for (int i = 0; i < boards.Length; i++)
        {
            if (boards[i] != null)
                boards[i].Refresh();
        }
    }

    void Start()
    {
        EnsureUi();
        Refresh();
    }

    public void Refresh()
    {
        EnsureUi();
        if (bodyText == null)
            return;

        bodyText.text = BuildNewsText();
        AlignCanvasToNewsSurface();
    }

    string BuildNewsText()
    {
        var sb = new System.Text.StringBuilder(512);

        if (RoundResultStore.HasResult)
        {
            sb.AppendLine(RoundResultStore.IsVictory ? "VICTORIA" : "EXTRACCIÓN");
            sb.AppendLine(RoundResultStore.IsVictory
                ? "Habéis conseguido la foto de la acción polémica."
                : "Habéis huido sin la foto de la acción polémica.");
        }
        else
        {
            sb.AppendLine("NOTICIAS");
        }

        sb.AppendLine();
        sb.AppendLine(RunEconomyConstants.SectionTitle);

        if (RunEconomy.HasSettlement && RunEconomy.LastSettlement != null)
        {
            var lines = RunEconomy.LastSettlement.BreakdownLines;
            if (lines.Count == 0)
            {
                sb.AppendLine("(Sin fotos con valor)");
                sb.AppendLine($"{RunEconomyConstants.RunTotalLabel} -> 0");
                sb.AppendLine($"{RunEconomyConstants.AccumulatedLabel} -> {RunEconomy.LastSettlement.AccumulatedBefore}");
                sb.AppendLine($"{RunEconomyConstants.GrandTotalLabel} -> {RunEconomy.LastSettlement.GrandTotal}");
            }
            else
            {
                for (int i = 0; i < lines.Count; i++)
                    sb.AppendLine(lines[i]);
            }
        }
        else
        {
            sb.AppendLine("(Liquidación pendiente)");
            sb.AppendLine($"{RunEconomyConstants.AccumulatedLabel} -> {RunEconomy.Balance}");
        }

        return sb.ToString().TrimEnd();
    }

    void EnsureUi()
    {
        if (built && bodyText != null)
        {
            AlignCanvasToNewsSurface();
            return;
        }

        newsRoot = transform;
        if (newsRoot.name != newsObjectName)
        {
            GameObject found = GameObject.Find(newsObjectName);
            if (found != null)
                newsRoot = found.transform;
        }

        if (newsRoot == null)
        {
            Debug.LogWarning("[NewsBoard] No se encontró el objeto News.");
            return;
        }

        Transform existing = newsRoot.Find("NewsCanvas");
        if (existing != null)
        {
            bodyText = existing.GetComponentInChildren<Text>(true);
            built = bodyText != null;
            AlignCanvasToNewsSurface();
            return;
        }

        var canvasGo = new GameObject("NewsCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(newsRoot, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = canvasSize;

        var textGo = new GameObject("Body", typeof(Text));
        textGo.transform.SetParent(canvasGo.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(40f, 40f);
        textRt.offsetMax = new Vector2(-40f, -40f);

        bodyText = textGo.GetComponent<Text>();
        bodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (bodyText.font == null)
            bodyText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        bodyText.fontSize = 42;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        bodyText.color = Color.white;
        bodyText.raycastTarget = false;

        built = true;
        AlignCanvasToNewsSurface();
    }

    void AlignCanvasToNewsSurface()
    {
        if (newsRoot == null)
            return;

        Transform canvasT = newsRoot.Find("NewsCanvas");
        if (canvasT == null)
            return;

        Vector3 normal = GetNewsSurfaceNormal(newsRoot);
        Vector3 upHint = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (upHint.sqrMagnitude < 0.001f)
            upHint = Vector3.ProjectOnPlane(newsRoot.right, normal);
        if (upHint.sqrMagnitude < 0.001f)
            upHint = newsRoot.right;

        // Canvas mira hacia la cámara al otro lado de la normal (texto sobre la cara frontal).
        Quaternion worldRot = Quaternion.LookRotation(-normal, upHint.normalized);
        Vector3 worldPos = newsRoot.position + normal * surfaceOffset;

        canvasT.SetPositionAndRotation(worldPos, worldRot);
        canvasT.localScale = Vector3.one * canvasScale;
    }

    /// <summary>
    /// Normal world-space del plano News. Mesh primero; Plane de Unity → transform.up.
    /// </summary>
    static Vector3 GetNewsSurfaceNormal(Transform news)
    {
        if (news.TryGetComponent(out MeshFilter meshFilter)
            && meshFilter.sharedMesh != null
            && meshFilter.sharedMesh.normals != null
            && meshFilter.sharedMesh.normals.Length > 0)
        {
            return news.TransformDirection(meshFilter.sharedMesh.normals[0]).normalized;
        }

        if (news.TryGetComponent(out MeshCollider meshCollider)
            && meshCollider.sharedMesh != null
            && meshCollider.sharedMesh.normals != null
            && meshCollider.sharedMesh.normals.Length > 0)
        {
            return news.TransformDirection(meshCollider.sharedMesh.normals[0]).normalized;
        }

        // Built-in Plane: normal local +Y.
        return news.up.normalized;
    }
}
