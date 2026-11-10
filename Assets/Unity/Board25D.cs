using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2.5D 棋盘（2026-11-09 从零起步）：云顶之弈式斜俯视牌桌——3D 牌、3D 英雄台座、
    /// 手牌扇形摆放、悬停高亮。引擎（GameEngine2/3）完全不动，本类只是新的"表现层"：
    /// 读引擎状态 → 摆 3D；点 3D 牌/按钮 → 调引擎。uGUI 只用来做底部按钮菜单，牌桌本体全 3D。
    ///
    /// 阶段1（本次）：牌桌/相机/光照、3D 牌（卡面色+图标+牌名）、手牌扇形布局、
    /// 英雄台座（名字/HP/MP/状态）、牌堆/弃牌堆、点击手牌→行动菜单、Pending 抉择菜单、
    /// 结束回合、终局面板（再来一局/回主菜单）。3D 指向攻击箭头、动画飞行、3代法术特效留后续迭代。
    /// </summary>
    public class Board25D : MonoBehaviour
    {
        GameEngine2 _e;
        GameEngine3 _g3;
        IGameSession _session;
        int _me;
        int _seatCount;
        Font _font;
        Camera _cam;

        // 3D 物体缓存（Refresh 全量重建，数量小，无所谓性能）
        readonly List<GameObject> _transient = new List<GameObject>();   // 每次 Refresh 清空重建的东西
        readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>();
        readonly Dictionary<int, Texture2D> _iconTex = new Dictionary<int, Texture2D>();

        // 悬停
        Card _hover;

        // ---- 屏幕层（uGUI 只做按钮和文字，牌桌在 3D）----
        Text _topText;
        Transform _menuPanel;        // 底部行动/抉择菜单（动态按钮）
        Button _endTurnBtn;

        class SeatView
        {
            public Vector3 Anchor;   // 座位锚点（椭圆上）
            public Vector3 Facing;   // 朝向牌桌中心的单位向量（xz）
            public float Yaw;        // 座位朝向（度）
        }
        SeatView[] _seats;

        // ---- 抽卡飞行动画（2026-11-09②）：减法原则，只做"新牌从来源飞入" ----
        static readonly Vector3 DeckPos = new Vector3(-1.5f, 0.6f, 0.3f);    // 牌堆顶（飞行起点）
        readonly Dictionary<int, Vector3> _lastPos = new Dictionary<int, Vector3>();   // cardId → 上次 Refresh 的世界位置（=飞行起点）
        readonly HashSet<int> _landing = new HashSet<int>();                             // 飞行中、落点前暂隐的牌
        readonly Dictionary<int, GameObject> _spawnedNow = new Dictionary<int, GameObject>();   // 本次 Refresh 的 牌id→GO
        bool _firstRefresh = true;

        // ==================================================================
        // 初始化
        // ==================================================================

        public void Init(GameEngine2 engine, IGameSession session, int me)
        {
            _e = engine;
            _g3 = engine as GameEngine3;
            _session = session;
            _me = me;
            _seatCount = engine.Players.Count;
            _font = UIKit.LoadFont();
            UIKit.EnsureEventSystem();
            BuildScene();
            BuildOverlay();
            Refresh();
        }

        void BuildScene()
        {
            // 相机：TFT 式斜俯视（南高北低看牌桌），按人数微调
            var camGO = new GameObject("BoardCam");
            _cam = camGO.AddComponent<Camera>();
            _cam.tag = "MainCamera";
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            _cam.fieldOfView = _seatCount >= 4 ? 52f : 45f;
            float back = _seatCount >= 4 ? 13.2f : 10.6f;
            camGO.transform.position = new Vector3(0f, 10.8f, -back);
            camGO.transform.eulerAngles = new Vector3(56f, 0f, 0f);
            // 不加 AudioListener：场景主相机已有一个，重复会刷"2 audio listeners"警告

            // 光照：主光斜射 + 环境光补光（无美术资源，靠光照层次出体积感）
            var lightGO = new GameObject("Sun");
            var sun = lightGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.05f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            lightGO.transform.eulerAngles = new Vector3(50f, -28f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.4f, 0.46f);

            // 桌面
            float tw = _seatCount >= 4 ? 15.5f : 13.5f;
            TableCube("Table", new Vector3(0, -0.16f, 0.3f), new Vector3(tw, 0.32f, 10f),
                      new Color(0.13f, 0.2f, 0.22f));
            // 桌沿装饰线（两条浅色细条，假装有边框）
            TableCube("RimN", new Vector3(0, 0.02f, 5.05f), new Vector3(tw, 0.05f, 0.18f), new Color(0.55f, 0.45f, 0.25f));
            TableCube("RimS", new Vector3(0, 0.02f, -4.45f), new Vector3(tw, 0.05f, 0.18f), new Color(0.55f, 0.45f, 0.25f));

            // 座位布局：自己固定在南方（近镜头侧），其余绕椭圆均匀分布
            _seats = new SeatView[_seatCount];
            float rx = _seatCount >= 4 ? 6.2f : 4.9f;
            float rz = _seatCount >= 4 ? 3.6f : 3.1f;
            for (int s = 0; s < _seatCount; s++)
            {
                float ang = -90f + s * (360f / _seatCount);   // 座位0=南
                float rad = ang * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Cos(rad) * rx, 0f, Mathf.Sin(rad) * rz);
                var f = (new Vector3(0f, 0f, 0.3f) - a);
                f.y = 0f; f.Normalize();
                _seats[s] = new SeatView { Anchor = a, Facing = f, Yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg };
            }
        }

        void BuildOverlay()
        {
            var canvasGO = new GameObject("BoardCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGO.transform;

            var top = UIKit.UIObj("TopBar", root);
            UIKit.SetRect(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-500, -56), new Vector2(500, -8));
            _topText = UIKit.AddText(top, "", 24, new Color(0.92f, 0.92f, 0.95f), TextAnchor.MiddleCenter, _font);

            var menu = UIKit.UIObj("MenuPanel", root);
            UIKit.SetRect(menu, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-560, 70), new Vector2(560, 170));
            UIKit.AddImage(menu, new Color(0.09f, 0.1f, 0.14f, 0.88f));
            var hlg = menu.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.padding = new RectOffset(16, 16, 8, 8);
            _menuPanel = menu.transform;

            var endGO = UIKit.UIObj("EndTurnBtn", root);
            UIKit.SetRect(endGO, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-220, 76), new Vector2(-40, 156));
            _endTurnBtn = UIKit.MakeButton(endGO.transform, "结束回合", OnEndTurn,
                new Color(0.55f, 0.35f, 0.2f), 24, _font);
        }

        // ==================================================================
        // Refresh：引擎状态 → 3D
        // ==================================================================

        public void Refresh()
        {
            var prev = new Dictionary<int, Vector3>(_lastPos);   // 飞行起点 = 牌上次出现的位置
            _spawnedNow.Clear();

            foreach (var go in _transient) if (go != null) Destroy(go.gameObject);
            _transient.Clear();
            _hover = null;

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me;
            _legal = (_e.GameOver || _e.Pending != null)
                ? new List<GameAction>()
                : _e.GetLegalActions(_me);

            var cur = _e.Players[_e.Current];
            _topText.text = _e.GameOver
                ? "对 局 结 束"
                : _e.InMulligan
                    ? "开局调度阶段 · " + (cur.IsHuman ? "轮到你了" : cur.Name + " 调度中…")
                    : string.Format("第 {0} 轮 · {1}    MP {2}/{3}", _e.TurnNumber,
                        cur.IsHuman ? "你的回合" : cur.Name + " 的回合",
                        _e.Players[_me].MP, _e.Players[_me].MaxMP);
            _endTurnBtn.interactable = myTurn && _legal.Any(a => a.Kind == ActionKind.EndTurn);

            // 牌堆 / 弃牌堆（桌中央两侧）
            Pile(new Vector3(-1.5f, 0f, 0.3f), false, "牌堆 " + _e.SideDeck.Count);
            Pile(new Vector3(1.5f, 0f, 0.3f), true, "弃牌 " + _e.Discard.Count);

            // 每位玩家：英雄台座 + 手牌
            for (int s = 0; s < _seatCount; s++)
            {
                var p = _e.Players[s];
                var seat = _seats[s];
                HeroPedestal(p, seat);
                if (!p.Alive) continue;
                if (s == _me) LayoutMyHand(p, seat);
                else LayoutOpponentHand(p, seat);
            }

            // 新入手的牌 → 飞入动画：来源=上次位置（被勒索/复活回手也有正确轨迹），没见过=牌堆
            if (!_firstRefresh && !_e.InMulligan)
            {
                foreach (var p in _e.Players)
                {
                    if (!p.Alive) continue;
                    foreach (var c in p.Hand)
                    {
                        if (prev.ContainsKey(c.Id) || _landing.Contains(c.Id)) continue;
                        GameObject target;
                        if (!_spawnedNow.TryGetValue(c.Id, out target) || target == null) continue;
                        Vector3 from;
                        if (!prev.TryGetValue(c.Id, out from)) from = DeckPos;
                        StartCoroutine(FlightCard(c, from, target));
                    }
                }
            }
            _firstRefresh = false;

            // 底部菜单：Pending 优先，否则关闭（点开牌时由点击事件重建）
            BuildMenu();
        }

        /// <summary>抽卡/回手飞行动画：牌背朝上从来源飞出，抛物线落位后目标牌弹一下现身。
        /// 飞行牌不进 _transient（不随 Refresh 销毁），落点目标若被后续 Refresh 重建则直接跳过。</summary>
        System.Collections.IEnumerator FlightCard(Card c, Vector3 from, GameObject target)
        {
            _landing.Add(c.Id);
            var flight = SpawnCard(c, false, from, 0f, isMine: false, transient: false);
            var fcol = flight.GetComponent<Collider>();
            if (fcol != null) Destroy(fcol);   // 飞行中不挡射线/不可点
            Vector3 to = target.transform.position;
            float yawEnd = target.transform.eulerAngles.y;
            const float dur = 0.5f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                if (flight == null) { _landing.Remove(c.Id); yield break; }
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                flight.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.1f);
                var e = flight.transform.eulerAngles;
                flight.transform.eulerAngles = new Vector3(e.x, Mathf.LerpAngle(0f, yawEnd, k), e.z);
                yield return null;
            }
            Destroy(flight);
            _landing.Remove(c.Id);
            if (target == null) yield break;   // 已被后续 Refresh 重建（重建即可见），无需收尾
            target.SetActive(true);
            const float pop = 0.16f;           // 落点轻弹
            for (float t = 0f; t < pop; t += Time.deltaTime)
            {
                if (target == null) yield break;
                target.transform.localScale = Vector3.Lerp(Vector3.one * 1.18f, Vector3.one, t / pop);
                yield return null;
            }
            if (target != null) target.transform.localScale = Vector3.one;
        }

        List<GameAction> _legal;

        void LayoutMyHand(PlayerState p, SeatView seat)
        {
            var hand = p.Hand.OrderBy(c => c.Id).ToList();
            int n = hand.Count;
            if (n == 0) return;
            // 扇形向镜头侧张开：位置 = 枢轴 + 朝向反方向偏转 a 角 ×半径（此前算成了 +朝向，整副牌甩到对面去了）
            var pivot = seat.Anchor + seat.Facing * 2.2f;
            float radius = 3.2f;
            float step = n <= 1 ? 0f : Mathf.Clamp(84f / (n - 1), 7f, 15f);
            for (int i = 0; i < n; i++)
            {
                float a = (i - (n - 1) / 2f) * step;
                var dir = Quaternion.Euler(0f, a, 0f) * (-seat.Facing);
                var pos = pivot + dir * radius;
                var card = SpawnCard(hand[i], true, pos, a, isMine: true);
                card.name = "MyCard_" + hand[i].Id;
            }
        }

        void LayoutOpponentHand(PlayerState p, SeatView seat)
        {
            var hand = p.Hand;
            int n = hand.Count;
            if (n == 0) return;
            // 牌列贴着各自座位、不横穿牌桌：间距随张数收缩，最多占 7 单位宽
            var center = seat.Anchor + seat.Facing * 1.1f;
            var right = Quaternion.Euler(0, 90, 0) * seat.Facing;
            float spacing = Mathf.Min(1.0f, 7.0f / Mathf.Max(n, 1));
            for (int i = 0; i < n; i++)
            {
                var pos = center + right * (i - (n - 1) / 2f) * spacing;
                SpawnCard(hand[i], false, pos, seat.Yaw, isMine: false);
            }
        }

        void HeroPedestal(PlayerState p, SeatView seat)
        {
            var pos = seat.Anchor + seat.Facing * 0.3f;
            var ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ped.name = "Hero_" + p.Seat;
            ped.transform.position = pos + new Vector3(0, 0.09f, 0);
            ped.transform.localScale = new Vector3(0.95f, 0.09f, 0.95f);
            ped.GetComponent<Renderer>().sharedMaterial = Mat("ped_" + p.Seat, p.Alive
                ? new Color(0.35f, 0.28f, 0.18f) : new Color(0.15f, 0.13f, 0.13f));
            _transient.Add(ped);

            string status = "";
            if (_e.IsSilenced(p.Seat)) status += " ·沉默";
            if (!p.Alive) status = " ·出局";
            string txt = p.Alive
                ? string.Format("{0}【{1}】\nHP {2}/{3}  MP {4}/{5}{6}", p.Name, p.HeroName,
                    Mathf.Max(p.HP, 0), p.MaxHP, p.MP, p.MaxMP, status)
                : p.Name + "\n☠ 出局";
            // TextMesh 可读面是本地 -Z（2026-11-09 实测：y=180 会镜像），y=0 正对镜头
            var tm = Text3D(txt, pos + new Vector3(0, 1.15f, 0), new Vector3(0, 0, 0), 0.16f,
                p.Alive ? new Color(0.95f, 0.92f, 0.8f) : new Color(0.5f, 0.35f, 0.35f));
            _transient.Add(tm.gameObject);
        }

        void Pile(Vector3 pos, bool isDiscard, string label)
        {
            // 有厚度的牌堆：按剩余数量叠层，每层一张薄牌、轻微错角（像真实牌叠）
            int count = isDiscard ? _e.Discard.Count : _e.SideDeck.Count;
            int layers = count <= 0 ? 0 : Mathf.Clamp(count, isDiscard ? 2 : 4, isDiscard ? 10 : 24);
            for (int i = 0; i < layers; i++)
            {
                var layer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                layer.name = (isDiscard ? "DiscardL" : "DeckL") + i;
                layer.transform.position = pos + new Vector3(0, 0.014f + i * 0.024f, 0);
                layer.transform.eulerAngles = new Vector3(0, (i * 37 % 14) - 7f, 0);
                layer.transform.localScale = new Vector3(1.05f, 0.02f, 1.5f);
                layer.GetComponent<Renderer>().sharedMaterial = Mat(isDiscard ? "pile_disc" : "pile_deck",
                    isDiscard ? new Color(0.25f, 0.2f, 0.3f) : new Color(0.18f, 0.15f, 0.34f));
                var lcol = layer.GetComponent<Collider>();
                if (lcol != null) lcol.enabled = false;
                _transient.Add(layer);
            }
            float topY = 0.35f + layers * 0.024f;
            var tm = Text3D(label, pos + new Vector3(0, topY, 0), new Vector3(0, 0, 0), 0.13f,
                new Color(0.75f, 0.78f, 0.85f));
            _transient.Add(tm.gameObject);
            // 弃牌堆顶牌亮出来（信息 already 公开，并登记位置——复活动画的起点）
            if (isDiscard && _e.Discard.Count > 0)
            {
                var top = _e.Discard[_e.Discard.Count - 1];
                SpawnCard(top, true, pos + new Vector3(0, 0.03f + layers * 0.024f, 0), 0f, isMine: false);
            }
        }

        // ==================================================================
        // 3D 牌工厂
        // ==================================================================

        /// <param name="transient">true=牌桌常驻牌（入 _transient 随 Refresh 重建 + 登记位置/映射）；
        /// false=飞行动画用的临时牌，不登记不托管。</param>
        GameObject SpawnCard(Card c, bool faceUp, Vector3 pos, float yaw, bool isMine, bool transient = true)
        {
            var root = new GameObject("Card_" + c.Type + "_" + c.Id);
            root.transform.position = pos;
            root.transform.eulerAngles = new Vector3(0, yaw, 0);
            root.transform.localScale = Vector3.one;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.05f, 0.035f, 1.5f);
            body.GetComponent<Collider>().enabled = false;   // 命中判定统一用根的 BoxCollider

            if (faceUp)
            {
                var def = CardVisual.Of(c.Type);
                body.GetComponent<Renderer>().sharedMaterial = Mat("cb_" + c.Type, def.Bg);
                var icon = IconTexture(c.Type);
                if (icon != null)
                    QuadOn(root.transform, "Icon", new Vector3(0, 0.021f, 0.3f), new Vector3(90, 0, 0), 0.6f, 0.6f,
                        Mat("ci_" + c.Type, Color.white, icon));
                // 平铺文字可读面朝上、字顶朝北：名字放近玩家侧（南）
                var nameTxt = Text3D(c.Type.Name(), new Vector3(0, 0, 0), new Vector3(90, 0, 0), 0.115f, def.Text);
                nameTxt.transform.SetParent(root.transform, false);
                nameTxt.transform.localPosition = new Vector3(0, 0.022f, -0.38f);
            }
            else
            {
                // 牌背：深色底 + 内框（无文字——此前逐张"3D杀"平铺连成"染色体"横纹）
                body.GetComponent<Renderer>().sharedMaterial = Mat("cardback",
                    new Color(0.18f, 0.15f, 0.34f));
                QuadOn(root.transform, "BackFrame", new Vector3(0, 0.02f, 0), new Vector3(90, 0, 0), 0.88f, 1.3f,
                    Mat("cardback_frame", new Color(0.26f, 0.21f, 0.44f)));
                QuadOn(root.transform, "BackCore", new Vector3(0, 0.021f, 0), new Vector3(90, 0, 0), 0.5f, 0.8f,
                    Mat("cardback_core", new Color(0.2f, 0.16f, 0.38f)));
            }

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.1f, 0.12f, 1.55f);
            var view = root.AddComponent<CardView25D>();
            view.Card = c; view.Mine = isMine; view.FaceUp = faceUp;
            view.Home = pos;
            if (transient)
            {
                _transient.Add(root);
                _spawnedNow[c.Id] = root;
                _lastPos[c.Id] = pos;
                if (_landing.Contains(c.Id)) root.SetActive(false);   // 飞行中：落点牌先藏着，落地弹现
            }
            return root;
        }

        // ==================================================================
        // 每帧：悬停 + 点击
        // ==================================================================

        void Update()
        {
            if (_cam == null) return;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Card hit = null;
            if (!overUI)
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit rh;
                if (Physics.Raycast(ray, out rh))
                {
                    var view = rh.collider.GetComponent<CardView25D>();
                    if (view != null && view.Mine && view.FaceUp) hit = view.Card;
                }
            }
            if (hit != _hover) _hover = hit;

            foreach (var go in _transient)
            {
                if (go == null) continue;
                var view = go.GetComponent<CardView25D>();
                if (view == null || !view.Mine) continue;
                bool hov = _hover == view.Card;
                float targetY = view.Home.y + (hov ? 0.22f : 0f);
                var p = go.transform.position;
                go.transform.position = new Vector3(p.x, Mathf.Lerp(p.y, targetY, Time.deltaTime * 12f), p.z);
                float targetScale = hov ? 1.12f : 1f;
                go.transform.localScale = Vector3.Lerp(go.transform.localScale, Vector3.one * targetScale, Time.deltaTime * 12f);
            }

            if (Input.GetMouseButtonDown(0) && !overUI && _hover != null
                && !_e.GameOver && _e.Pending == null && _e.Current == _me)
                OpenCardMenu(_hover);
        }

        // ==================================================================
        // 菜单（行动 / 抉择 / 终局）
        // ==================================================================

        void ClearMenu()
        {
            foreach (Transform child in _menuPanel) Destroy(child.gameObject);
        }

        void AddMenuButton(string label, Color bg, System.Action act)
        {
            var btn = UIKit.MakeButton(_menuPanel, label, () => { act(); Refresh(); }, bg, 19, _font);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 240;
        }

        void BuildMenu()
        {
            ClearMenu();
            if (_e.GameOver)
            {
                var alive = _e.Players.FirstOrDefault(p => p.Alive);
                _topText.text = "对局结束 · 胜者：" + (alive != null ? alive.Name : "无");
                AddMenuButton("再来一局", new Color(0.3f, 0.5f, 0.35f), () => _session.Restart());
                AddMenuButton("返回主菜单", new Color(0.5f, 0.3f, 0.3f), () => _session.BackToMenu());
                return;
            }
            var pc = _e.Pending;
            if (pc != null && pc.Seat == _me) { BuildPendingMenu(pc); return; }

            // 自己的回合：常设入口（研习/合成/英雄技不绑单张牌）
            if (_e.Current == _me && _e.Pending == null)
            {
                if (_legal.Any(a => a.Kind == ActionKind.Learn))
                    AddMenuButton("研 习", new Color(0.3f, 0.55f, 0.65f), OpenLearnMenu);
                if (_legal.Any(a => a.Kind == ActionKind.Craft))
                    AddMenuButton("合 成", new Color(0.55f, 0.4f, 0.25f), OpenCraftMenu);
                if (_legal.Any(a => a.Kind == ActionKind.HeroPower))
                    AddMenuButton("英雄技", new Color(0.6f, 0.5f, 0.2f), () =>
                        Apply(_legal.First(a => a.Kind == ActionKind.HeroPower)));
            }
        }

        void OpenCardMenu(Card c)
        {
            ClearMenu();
            var acts = _legal.Where(a => a.SourceCardId == c.Id).ToList();
            // 研习：learn 行动不带 SourceCardId，按法术类型对到手上的法术牌
            acts.AddRange(_legal.Where(a => a.Kind == ActionKind.Learn && a.TargetType == c.Type));
            var def = CardDefs.Of(c.Type);

            if (acts.Count == 0)
            {
                string desc = def.HasActiveSkill ? def.SkillDesc : def.DisplayDesc;
                AddMenuButton("【" + c.Type.Name() + "】" + def.PassiveDesc, new Color(0.3f, 0.32f, 0.4f), () => { });
                if (!string.IsNullOrEmpty(desc) && desc != def.PassiveDesc)
                    AddMenuButton(desc, new Color(0.3f, 0.32f, 0.4f), () => { });
            }
            else
            {
                foreach (var a in acts)
                {
                    var act = a;
                    AddMenuButton(DescAction(act), new Color(0.35f, 0.42f, 0.6f), () => Apply(act));
                }
            }
            AddMenuButton("取消", new Color(0.4f, 0.28f, 0.28f), BuildMenu);
        }

        void OpenLearnMenu()
        {
            ClearMenu();
            foreach (var a in _legal.Where(x => x.Kind == ActionKind.Learn))
            {
                var act = a;
                var vis = CardVisual.Of(act.TargetType);
                AddMenuButton("研习【" + act.TargetType.Name() + "】", vis.Bg, () => Apply(act));
            }
            AddMenuButton("取消", new Color(0.4f, 0.28f, 0.28f), BuildMenu);
        }

        void OpenCraftMenu()
        {
            ClearMenu();
            foreach (var a in _legal.Where(x => x.Kind == ActionKind.Craft))
            {
                var act = a;
                AddMenuButton("合成【" + act.TargetType.Name() + "】", new Color(0.55f, 0.4f, 0.25f), () => Apply(act));
            }
            AddMenuButton("取消", new Color(0.4f, 0.28f, 0.28f), BuildMenu);
        }

        void BuildPendingMenu(PendingChoice pc)
        {
            switch (pc.Kind)
            {
                case ChoiceKind.GuardIntercept:
                    _topText.text = "【" + (pc.DrawnCard != null ? pc.DrawnCard.Type.Name() : "?") + "】被抽中！用谁挡刀？";
                    foreach (var ic in pc.Interceptors)
                    {
                        var card = ic;
                        AddMenuButton("【" + card.Type.Name() + "】挡刀", new Color(0.5f, 0.35f, 0.2f),
                            () => _e.SubmitGuardChoice(true, card.Id));
                    }
                    AddMenuButton("不挡", new Color(0.4f, 0.28f, 0.28f), () => _e.SubmitGuardChoice(false));
                    break;
                case ChoiceKind.PickFromDiscard:
                    _topText.text = pc.Reason + "：从弃牌堆选 1 张";
                    foreach (var opt in pc.Options)
                    {
                        var card = opt;
                        AddMenuButton("回收【" + card.Type.Name() + "】", new Color(0.3f, 0.45f, 0.35f),
                            () => _e.SubmitDiscardPick(card.Id));
                    }
                    break;
                case ChoiceKind.DeathrattleAttack:
                    _topText.text = "带刀侍卫亡语：选择攻击目标";
                    foreach (var t in _e.Players.Where(p => p.Alive && p.Seat != pc.Seat))
                    {
                        var seat = t.Seat;
                        AddMenuButton("攻击 " + t.Name, new Color(0.6f, 0.3f, 0.25f),
                            () => _e.SubmitDeathrattleTarget(seat));
                    }
                    break;
                case ChoiceKind.CometCounter:
                    _topText.text = "有法术被释放！是否消耗 1 张【彗星】抵消？";
                    AddMenuButton("消耗彗星抵消", new Color(0.3f, 0.5f, 0.35f), () => _g3.SubmitCometChoice(true));
                    AddMenuButton("不抵消", new Color(0.4f, 0.28f, 0.28f), () => _g3.SubmitCometChoice(false));
                    break;
                case ChoiceKind.ClarityPick:
                    _topText.text = "清晰术：看牌库顶 3 张，选 1 张加入手牌";
                    foreach (var opt in pc.Options)
                    {
                        var card = opt;
                        AddMenuButton("拿【" + card.Type.Name() + "】", new Color(0.3f, 0.5f, 0.6f),
                            () => _g3.SubmitClarityPick(card.Id));
                    }
                    break;
                case ChoiceKind.IgnitePick:
                    _topText.text = "点燃：" + pc.Reason + " 的手牌中选 1 张缠上火焰";
                    foreach (var opt in pc.Options)
                    {
                        var card = opt;
                        AddMenuButton("点燃【" + card.Type.Name() + "】", new Color(0.8f, 0.35f, 0.15f),
                            () => _g3.SubmitIgnitePick(card.Id));
                    }
                    break;
            }
        }

        void OnEndTurn()
        {
            if (_endTurnBtn.interactable)
                Apply(new GameAction { Kind = ActionKind.EndTurn });
        }

        void Apply(GameAction act)
        {
            _e.ApplyAction(act);
            Refresh();
        }

        // ==================================================================
        // 小件工厂
        // ==================================================================

        string DescAction(GameAction a)
        {
            string tn = a.TargetSeat >= 0 ? _e.Players[a.TargetSeat].Name : "";
            switch (a.Kind)
            {
                case ActionKind.Attack: return "攻击 " + tn;
                case ActionKind.ArsenalAttack: return "军火库追击→" + tn;
                case ActionKind.PoisonAttack: return "毒药→" + tn;
                case ActionKind.Exorcise: return "驱魔→" + tn;
                case ActionKind.Seize: return "沉默→" + tn;
                case ActionKind.Antidote: return "解药·回收";
                case ActionKind.Bribe: return "贿赂→" + tn;
                case ActionKind.Extort: return "勒索【" + a.TargetType.Name() + "】";
                case ActionKind.ArmGuard: return "武装侍卫与大臣";
                case ActionKind.Hide: return "藏起";
                case ActionKind.Craft: return "合成【" + a.TargetType.Name() + "】";
                case ActionKind.SynthRevive: return "复活";
                case ActionKind.SynthPrayer: return "祈祷";
                case ActionKind.SynthHostage: return "人质→" + tn;
                case ActionKind.SynthJudge: return "审判→" + tn;
                case ActionKind.BombPlant: return "安放炸弹";
                case ActionKind.Deathblow: return "致命一击→" + tn;
                case ActionKind.Mulligan: return "调度：换掉这张";
                case ActionKind.EndMulligan: return "结束调度";
                case ActionKind.Learn: return "研习【" + a.TargetType.Name() + "】";
                case ActionKind.CastSpell: return "释放【" + a.TargetType.Name() + "】" + (tn != "" ? "→" + tn : "");
                default: return a.Kind.ToString();
            }
        }

        void TableCube(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(name, color);
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        TextMesh Text3D(string s, Vector3 worldPos, Vector3 euler, float charSize, Color color)
        {
            var go = new GameObject("Txt_" + s);
            go.transform.position = worldPos;
            go.transform.eulerAngles = euler;
            var tm = go.AddComponent<TextMesh>();
            tm.text = s;
            tm.font = _font;
            tm.fontSize = 48;
            tm.characterSize = charSize;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            return tm;
        }

        void QuadOn(Transform parent, string name, Vector3 localPos, Vector3 localEuler, float w, float h, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = new Vector3(w, h, 1f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        Material Mat(string key, Color color, Texture2D tex = null)
        {
            Material m;
            if (_matCache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(Shader.Find("Standard"));
            m.color = color;
            if (tex != null) m.mainTexture = tex;
            _matCache[key] = m;
            return m;
        }

        Texture2D IconTexture(CardType t)
        {
            Texture2D tex;
            if (_iconTex.TryGetValue((int)t, out tex)) return tex;
            var sprite = CardVisual.IconOf(t);
            tex = sprite != null ? sprite.texture : null;
            _iconTex[(int)t] = tex;
            return tex;
        }
    }

    /// <summary>挂在 3D 牌根节点上：归属/朝向/悬停动画的家。</summary>
    public class CardView25D : MonoBehaviour
    {
        public Card Card;
        public bool Mine;
        public bool FaceUp;
        public Vector3 Home;
    }
}
