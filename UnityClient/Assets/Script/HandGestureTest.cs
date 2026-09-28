using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandGestureTest : MonoBehaviour
{
    [Header("비워두면 자동으로 찾음")]
    public OVRHand leftHand;
    public OVRHand rightHand;

    [Header("튜닝값")]
    public float curlDist = 0.11f;      // 손끝-손목 거리가 이보다 짧으면 '접힌 손가락'
    public float swipeSpeed = 0.8f;     // 왼손 스와이프 최소 속도 (m/s)
    public float swipeDist = 0.15f;     // 왼손 스와이프 최소 거리 (m)
    public float swipeCooldown = 0.5f;  // 연속 인식 방지 (초)

    [Header("도형 생성 및 발사 설정")]
    public float fistHoldDuration = 2.0f;   // 주먹 쥐고 유지하는 시간 (도형 변환)
    public float pointHoldDuration = 2.0f;  // 가리키고 유지하는 시간 (발사)
    public float shootSpeed = 8.0f;         // 발사 속도
    public float destroyDelay = 3.0f;       // 발사 후 소멸 시간

    OVRSkeleton leftSkel, rightSkel;
    Camera cam;
    TextMesh label;

    // 그리기 및 도형
    Transform drawRoot;
    LineRenderer currentLine;
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    Material lineMat;
    readonly Color[] colors = { Color.white, Color.red, Color.yellow, Color.green, Color.cyan, Color.magenta };
    int colorIdx;

    // 도형 오브젝트 상태
    GameObject activeShape;
    bool isShapeCreated = false;
    bool isShooting = false;

    // 타이머
    float fistTimer = 0f;
    float pointTimer = 0f;

    // 오른손 상태
    string rightState = "None", prevRightState = "None";
    Vector3 prevIndexTip;

    // 왼손 이동
    int lanePos = 2;
    Vector3 prevLeftPos;
    bool hasPrevLeft;
    float swipeAccum, lastSwipeTime;

    // 뼈 번호
    const int WRIST_O = 0,  WRIST_X = 1;
    const int THUMB_O = 19, THUMB_X = 5;
    const int INDEX_O = 20, INDEX_X = 10;
    const int MID_O   = 21, MID_X   = 15;
    const int RING_O  = 22, RING_X  = 20;
    const int PINKY_O = 23, PINKY_X = 25;

    void Start()
    {
        foreach (var h in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
        {
            var s = h.GetComponent<OVRSkeleton>();
            if (s == null) continue;
            if (s.GetSkeletonType().ToString().Contains("Left")) { if (!leftHand) leftHand = h; }
            else if (!rightHand) rightHand = h;
        }
        if (leftHand) leftSkel = leftHand.GetComponent<OVRSkeleton>();
        if (rightHand) rightSkel = rightHand.GetComponent<OVRSkeleton>();

        cam = Camera.main;
        drawRoot = new GameObject("Drawing").transform;
        lineMat = new Material(Shader.Find("Sprites/Default"));

        var lg = new GameObject("Label");
        lg.transform.SetParent(cam.transform, false);
        lg.transform.localPosition = new Vector3(0f, -0.2f, 0.7f);
        label = lg.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lg.GetComponent<MeshRenderer>().material = label.font.material;
        label.fontSize = 60;
        label.characterSize = 0.01f;
        label.anchor = TextAnchor.MiddleCenter;
    }

    void Update()
    {
        UpdateRight();
        UpdateLeft();

        string timerText = "";
        if (!isShapeCreated && fistTimer > 0f) timerText = $"\nFist Hold: {fistTimer:F1}s";
        else if (isShapeCreated && !isShooting && pointTimer > 0f) timerText = $"\nShoot Hold: {pointTimer:F1}s";

        label.text = $"Right: {rightState}\nPos: {lanePos}{timerText}";
    }

    // ---------------- 오른손 ----------------
    void UpdateRight()
    {
        if (!Ready(rightHand, rightSkel))
        {
            rightState = "None"; prevRightState = "None"; currentLine = null;
            fistTimer = 0f; pointTimer = 0f;
            return;
        }

        var s = rightSkel;
        bool idx = !Curled(s, INDEX_O, INDEX_X);
        bool mid = !Curled(s, MID_O, MID_X);
        bool ring = !Curled(s, RING_O, RING_X);
        bool pinky = !Curled(s, PINKY_O, PINKY_X);
        Vector3 tip = Bone(s, INDEX_O, INDEX_X);

        if (!idx && !mid && !ring && !pinky) rightState = "Fist";
        else if (rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index)) rightState = "Pinch";
        else if (idx && !mid && !ring && !pinky) rightState = "Point";
        else rightState = "Open";

        // 1. 주먹 2초 유지 -> 선 분석 후 도형으로 변환
        if (rightState == "Fist")
        {
            if (!isShapeCreated && lines.Count > 0)
            {
                fistTimer += Time.deltaTime;
                if (fistTimer >= fistHoldDuration)
                {
                    ConvertDrawingToShape();
                    fistTimer = 0f;
                }
            }
        }
        else
        {
            fistTimer = 0f;
        }

        // 2. 그리기 (핀치) - 도형이 발사 중이거나 이미 생성된 상태가 아닐 때
        if (rightState == "Pinch" && !isShapeCreated)
        {
            Draw(Vector3.Lerp(tip, Bone(s, THUMB_O, THUMB_X), 0.5f));
        }
        else
        {
            currentLine = null;
        }

        // 3. 검지 가리키기 (Point)
        if (rightState == "Point")
        {
            // 검지 이동량에 따라 그림 및 도형 이동
            if (prevRightState == "Point" && !isShooting)
            {
                drawRoot.position += (tip - prevIndexTip);
            }

            // 도형이 준비된 상태에서 검지 2초 유지 시 발사
            if (isShapeCreated && !isShooting)
            {
                pointTimer += Time.deltaTime;
                if (pointTimer >= pointHoldDuration)
                {
                    ShootShape();
                    pointTimer = 0f;
                }
            }
        }
        else
        {
            pointTimer = 0f;
        }

        prevIndexTip = tip;
        prevRightState = rightState;
    }

    void Draw(Vector3 worldPos)
    {
        if (currentLine == null)
        {
            var go = new GameObject("Stroke");
            go.transform.SetParent(drawRoot, false);
            currentLine = go.AddComponent<LineRenderer>();
            currentLine.useWorldSpace = false;
            currentLine.widthMultiplier = 0.01f;
            currentLine.material = lineMat;
            currentLine.startColor = currentLine.endColor = colors[colorIdx];
            currentLine.positionCount = 0;
            lines.Add(currentLine);
        }

        Vector3 p = drawRoot.InverseTransformPoint(worldPos);
        int n = currentLine.positionCount;
        if (n > 0 && Vector3.Distance(currentLine.GetPosition(n - 1), p) < 0.005f) return;
        currentLine.positionCount = n + 1;
        currentLine.SetPosition(n, p);
    }

    // ---------------- 도형 판별 및 변환 ----------------
    void ConvertDrawingToShape()
    {
        List<Vector3> allPoints = new List<Vector3>();
        foreach (var lr in lines)
        {
            if (lr == null) continue;
            Vector3[] pts = new Vector3[lr.positionCount];
            lr.GetPositions(pts);
            allPoints.AddRange(pts);
        }

        if (allPoints.Count < 5) return;

        // 경계 및 중심 계산
        Vector3 min = allPoints[0], max = allPoints[0];
        Vector3 center = Vector3.zero;
        float totalLength = 0f;

        for (int i = 0; i < allPoints.Count; i++)
        {
            center += allPoints[i];
            min = Vector3.Min(min, allPoints[i]);
            max = Vector3.Max(max, allPoints[i]);
            if (i > 0) totalLength += Vector3.Distance(allPoints[i], allPoints[i - 1]);
        }
        center /= allPoints.Count;
        Vector3 size = max - min;
        float radius = Mathf.Max(size.x, size.y, size.z) * 0.5f;

        // 도형 판별 알고리즘
        int corners = CountCorners(allPoints, 45f);
        float avgDist = 0f;
        foreach (var p in allPoints) avgDist += Vector3.Distance(p, center);
        avgDist /= allPoints.Count;

        float variance = 0f;
        foreach (var p in allPoints)
        {
            float d = Vector3.Distance(p, center);
            variance += Mathf.Abs(d - avgDist);
        }
        variance /= allPoints.Count;

        // 원/큐브/삼각형 분류
        PrimitiveType? primType = null;
        bool isTriangle = false;

        if (variance / Mathf.Max(avgDist, 0.01f) < 0.22f && corners <= 2)
        {
            primType = PrimitiveType.Sphere; // 원(구)
        }
        else if (corners == 3)
        {
            isTriangle = true; // 세모
        }
        else
        {
            primType = PrimitiveType.Cube; // 네모(큐브)
        }

        // 기존 라인 숨기기
        foreach (var lr in lines) if (lr != null) Destroy(lr.gameObject);
        lines.Clear();

        // 3D 도형 오브젝트 인스턴스화
        if (isTriangle)
        {
            activeShape = CreatePrism(Mathf.Max(radius * 1.5f, 0.08f));
        }
        else
        {
            activeShape = GameObject.CreatePrimitive(primType.Value);
            Destroy(activeShape.GetComponent<Collider>());
            float s = Mathf.Max(radius * 1.8f, 0.08f);
            activeShape.transform.localScale = new Vector3(s, s, s);
        }

        activeShape.transform.SetParent(drawRoot, false);
        activeShape.transform.localPosition = center;

        var rend = activeShape.GetComponent<MeshRenderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = colors[colorIdx];

        isShapeCreated = true;
    }

    // 꼭짓점(각도 급변) 감지
    int CountCorners(List<Vector3> pts, float minAngle)
    {
        if (pts.Count < 5) return 0;
        int corners = 0;
        int step = Mathf.Max(1, pts.Count / 30);

        for (int i = step; i < pts.Count - step; i += step)
        {
            Vector3 v1 = (pts[i] - pts[i - step]).normalized;
            Vector3 v2 = (pts[i + step] - pts[i]).normalized;
            float angle = Vector3.Angle(v1, v2);
            if (angle > minAngle && angle < 140f)
            {
                corners++;
                i += step * 2; // 중복 카운트 방지
            }
        }
        return corners;
    }

    // 삼각형 기둥 메쉬 절차적 생성
    GameObject CreatePrism(float size)
    {
        GameObject go = new GameObject("TrianglePrism");
        MeshFilter mf = go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        float h = size * 0.866f;
        float d = size * 0.2f;

        Vector3[] v = new Vector3[]
        {
            new Vector3(0, h * 0.5f, -d), new Vector3(size * 0.5f, -h * 0.5f, -d), new Vector3(-size * 0.5f, -h * 0.5f, -d), // 앞
            new Vector3(0, h * 0.5f, d),  new Vector3(size * 0.5f, -h * 0.5f, d),  new Vector3(-size * 0.5f, -h * 0.5f, d)   // 뒤
        };

        int[] tri = new int[]
        {
            0, 1, 2,  3, 5, 4,
            0, 4, 1,  0, 3, 4,
            1, 5, 2,  1, 4, 5,
            2, 3, 0,  2, 5, 3
        };

        mesh.vertices = v;
        mesh.triangles = tri;
        mesh.RecalculateNormals();
        mf.mesh = mesh;
        return go;
    }

    // ---------------- 발사 및 소멸 ----------------
    void ShootShape()
    {
        if (activeShape == null) return;
        isShooting = true;

        activeShape.transform.SetParent(null, true);
        Vector3 shootDir = cam.transform.forward;

        StartCoroutine(FlyAndDestroy(activeShape, shootDir));

        // 초기화
        activeShape = null;
        isShapeCreated = false;
        drawRoot.position = Vector3.zero;
    }

    IEnumerator FlyAndDestroy(GameObject target, Vector3 dir)
    {
        float timer = 0f;
        while (timer < destroyDelay)
        {
            if (target == null) yield break;
            target.transform.position += dir * shootSpeed * Time.deltaTime;
            target.transform.Rotate(Vector3.up, 360f * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null;
        }

        if (target != null) Destroy(target);
        isShooting = false;
    }

    // ---------------- 왼손 ----------------
    void UpdateLeft()
    {
        if (!Ready(leftHand, leftSkel)) { hasPrevLeft = false; swipeAccum = 0; return; }

        var s = leftSkel;
        bool open = !Curled(s, INDEX_O, INDEX_X) && !Curled(s, MID_O, MID_X)
                 && !Curled(s, RING_O, RING_X) && !Curled(s, PINKY_O, PINKY_X);
        Vector3 pos = Bone(s, WRIST_O, WRIST_X);

        if (!open || !hasPrevLeft) { swipeAccum = 0; prevLeftPos = pos; hasPrevLeft = true; return; }

        Vector3 right = cam.transform.right; right.y = 0; right.Normalize();
        float dx = Vector3.Dot(pos - prevLeftPos, right);
        prevLeftPos = pos;

        if (Mathf.Abs(dx) / Time.deltaTime < swipeSpeed) { swipeAccum = 0; return; }
        if (swipeAccum * dx < 0) swipeAccum = 0;
        swipeAccum += dx;

        if (Mathf.Abs(swipeAccum) >= swipeDist && Time.time - lastSwipeTime > swipeCooldown)
        {
            lanePos = Mathf.Clamp(lanePos + (swipeAccum > 0 ? 1 : -1), 1, 3);
            lastSwipeTime = Time.time;
            swipeAccum = 0;
        }
    }

    // ---------------- 공용 ----------------
    bool Ready(OVRHand h, OVRSkeleton s) =>
        h != null && s != null && h.IsTracked && s.IsInitialized && s.Bones.Count > 0;

    Vector3 Bone(OVRSkeleton s, int ovrIdx, int xrIdx) =>
        s.Bones[s.Bones.Count > 24 ? xrIdx : ovrIdx].Transform.position;

    bool Curled(OVRSkeleton s, int tipO, int tipX) =>
        Vector3.Distance(Bone(s, tipO, tipX), Bone(s, WRIST_O, WRIST_X)) < curlDist;
}