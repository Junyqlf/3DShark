using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 运行时代码生成的游戏界面（uGUI），无需任何场景预制。
    /// 布局：顶部信息栏 / 对手区 / 对局日志 / 手牌区+操作栏 / 中央抉择弹窗。
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        GameEngine _e;
        int _me;
        GameBootstrap _boot;
        Font _font;

        Text _turnText, _infoText, _hintText, _logText;
        RectTransform _oppArea, _handArea;
        Button _attackBtn, _endBtn, _cancelBtn;
        Transform _actionBar;
        GameObject _modal;
        Text _modalTitle, _modalBody;
        RectTransform _modalBtns;

        // 抉择按钮（直接放在操作栏，不依赖弹窗渲染）
        readonly List<GameObject> _pendingBtns = new List<GameObject>();
        string _pendingSig;      // 当前已渲染的抉择签名，避免每帧重复重建导致点不中
        bool _gameOverShown;

        // 中央提示横幅（攻击/技能反馈，自动淡出）
        Text _banner;
        float _bannerAlpha;

        readonly List<string> _log = new List<string>();
        List<GameAction> _legal = new List<GameAction>();
        List<GameAction> _targeting;   // 非null = 正在选目标

        const int LogKeep = 300;
        const int LogShow = 14;

        // ==================================================================
        // 弹窗（卡牌选择器）：内容区直接摆放可点击的卡牌视图，或少量文字按钮
        // ==================================================================

        void ShowModalBase(string title, string body)
        {
            _modal.SetActive(true);
            _modalTitle.text = title;
            _modalBody.text = body;
            foreach (Transform child in _modalBtns) Destroy(child.gameObject);
        }

        /// <summary>弹窗里放一张可点击的卡牌（侍卫挡刀/弃牌堆选牌用）。</summary>
        void AddModalCard(Card c, string caption, UnityAction onClick)
        {
            MakeCardView(c, _modalBtns, caption, true, true, 108, onClick);
        }

        /// <summary>弹窗里放一个文字按钮（女巫选药剂/终局再来一局用）。</summary>
        void AddModalTextButton(string label, UnityAction act)
        {
            var btn = MakeButton(_modalBtns, label, act, new Color(0.3f, 0.45f, 0.7f), 20);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
        }

        // ==================================================================
        // 初始化与界面搭建
        // ==================================================================

        public void Init(GameEngine engine, int humanSeat, GameBootstrap boot)
        {
            _e = engine;
            _me = humanSeat;
            _boot = boot;
            _font = LoadFont();

            _log.AddRange(_e.History);
            _e.OnLog += OnEngineLog;

            EnsureEventSystem();
            BuildCanvas();
            Refresh();
        }

        public static Font LoadFont()
        {
            // 内置字体不含中文字形，优先使用操作系统中文字体
            string[] cjk = { "Microsoft YaHei", "SimHei", "PingFang SC",
                             "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei" };
            var f = Font.CreateDynamicFontFromOSFont(cjk, 16);
            if (f != null) return f;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        }

        public static void EnsureEventSystem()
        {
#if UNITY_2022_2_OR_NEWER
            if (FindFirstObjectByType<EventSystem>() == null)
#else
            if (FindObjectOfType<EventSystem>() == null)
#endif
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        GameObject UIObj(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        Image AddImage(GameObject go, Color c)
        {
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        Text AddText(GameObject go, string content, int size, Color color, TextAnchor anchor)
        {
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = false;
            return t;
        }

        Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int fontSize = 20)
        {
            var go = UIObj("Btn_" + label, parent);
            AddImage(go, bg);
            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = cb;
            btn.onClick.AddListener(onClick);
            // 一个GameObject只能有一个Graphic组件：Image在按钮本体上，Text必须放到子物体
            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            return btn;
        }

        void BuildCanvas()
        {
            // 根 Canvas
            var canvasGO = new GameObject("SandKillCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGO.transform;

            // 背景
            var bg = UIObj("Background", root);
            SetRect(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(bg, new Color(0.11f, 0.12f, 0.15f));

            // 顶部信息栏
            var top = UIObj("TopBar", root);
            SetRect(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), Vector2.zero);
            AddImage(top, new Color(0.16f, 0.18f, 0.23f));

            var titleGO = UIObj("Title", top.transform);
            SetRect(titleGO, new Vector2(0, 0), new Vector2(0.15f, 1), new Vector2(16, 0), Vector2.zero);
            AddText(titleGO, "3 D 杀", 30, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleLeft);

            var turnGO = UIObj("TurnText", top.transform);
            SetRect(turnGO, new Vector2(0.15f, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero);
            _turnText = AddText(turnGO, "", 24, Color.white, TextAnchor.MiddleCenter);

            var infoGO = UIObj("InfoText", top.transform);
            SetRect(infoGO, new Vector2(0.55f, 0), new Vector2(0.91f, 1), Vector2.zero, Vector2.zero);
            _infoText = AddText(infoGO, "", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight);

            // 右上角退出按钮（打包成exe后玩家需要退出途径，Alt+F4不是人人都知道）
            var quitBtn = MakeButton(top.transform, "退出", OnQuitClicked, new Color(0.55f, 0.3f, 0.3f), 18);
            SetRect(quitBtn.gameObject, new Vector2(0.92f, 0.12f), new Vector2(1f, 0.88f), Vector2.zero, new Vector2(-12, 0));

            // 对手区（上部）
            var oppGO = UIObj("OpponentArea", root);
            SetRect(oppGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -300), new Vector2(-16, -72));
            _oppArea = (RectTransform)oppGO.transform;
            var oppHlg = oppGO.AddComponent<HorizontalLayoutGroup>();
            oppHlg.spacing = 16;
            oppHlg.childForceExpandWidth = false;
            oppHlg.childForceExpandHeight = true;
            oppHlg.childAlignment = TextAnchor.UpperCenter;

            // 日志区（中部）：纯文本直接显示最近若干行。
            // 不用ScrollRect/Mask/ContentSizeFitter——那条链在本项目环境中不可靠，草稿阶段越简单越好
            var logGO = UIObj("LogArea", root);
            SetRect(logGO, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 260), new Vector2(-16, -308));
            AddImage(logGO, new Color(0.07f, 0.08f, 0.1f));

            var logTextGO = UIObj("LogText", logGO.transform);
            SetRect(logTextGO, Vector2.zero, Vector2.one, new Vector2(12, 10), new Vector2(-12, -10));
            _logText = AddText(logTextGO, "", 19, new Color(0.85f, 0.88f, 0.92f), TextAnchor.UpperLeft);
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;

            // 手牌区（底部）
            var handGO = UIObj("HandArea", root);
            SetRect(handGO, new Vector2(0, 0), new Vector2(0.82f, 0), new Vector2(16, 16), new Vector2(-8, 236));
            AddImage(handGO, new Color(0.16f, 0.18f, 0.23f));
            _handArea = (RectTransform)handGO.transform;
            var handHlg = handGO.AddComponent<HorizontalLayoutGroup>();
            handHlg.spacing = 10;
            handHlg.padding = new RectOffset(10, 10, 10, 10);
            handHlg.childForceExpandWidth = false;
            handHlg.childForceExpandHeight = true;
            handHlg.childAlignment = TextAnchor.MiddleLeft;

            // 操作栏（右下）
            var barGO = UIObj("ActionBar", root);
            SetRect(barGO, new Vector2(0.82f, 0), new Vector2(1, 0), new Vector2(8, 16), new Vector2(-16, 236));
            var barVlg = barGO.AddComponent<VerticalLayoutGroup>();
            barVlg.spacing = 10;
            barVlg.childForceExpandWidth = true;
            barVlg.childForceExpandHeight = false;
            barVlg.childAlignment = TextAnchor.UpperCenter;
            _actionBar = barGO.transform;

            _attackBtn = MakeButton(barGO.transform, "攻  击", OnAttackClicked, new Color(0.85f, 0.3f, 0.25f), 24);
            _attackBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _endBtn = MakeButton(barGO.transform, "结束回合", OnEndTurnClicked, new Color(0.3f, 0.55f, 0.85f), 24);
            _endBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _cancelBtn = MakeButton(barGO.transform, "取消选目标", OnCancelTargetClicked, new Color(0.5f, 0.5f, 0.55f), 20);
            _cancelBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            _cancelBtn.gameObject.SetActive(false);

            var hintGO = UIObj("Hint", barGO.transform);
            hintGO.AddComponent<LayoutElement>().flexibleHeight = 1;
            _hintText = AddText(hintGO, "", 16, new Color(0.75f, 0.78f, 0.82f), TextAnchor.UpperLeft);
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hintText.verticalOverflow = VerticalWrapMode.Truncate;

            // 中央横幅：攻击/技能等关键动作的即时反馈，自动淡出（不拦截点击）
            var bannerGO = UIObj("Banner", root);
            SetRect(bannerGO, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
                new Vector2(-600, -50), new Vector2(600, 50));
            _banner = AddText(bannerGO, "", 40, new Color(1f, 0.85f, 0.3f, 0f), TextAnchor.MiddleCenter);
            _banner.raycastTarget = false;

            BuildModal(root);
        }

        void BuildModal(RectTransform root)
        {
            // 全屏遮罩
            _modal = UIObj("Modal", root);
            SetRect(_modal, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(_modal, new Color(0, 0, 0, 0.65f));

            var panel = UIObj("Panel", _modal.transform);
            SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, -270), new Vector2(400, 270));
            AddImage(panel, new Color(0.18f, 0.2f, 0.25f));

            var titleGO = UIObj("Title", panel.transform);
            SetRect(titleGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -64), new Vector2(-20, -12));
            _modalTitle = AddText(titleGO, "", 26, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter);

            var bodyGO = UIObj("Body", panel.transform);
            SetRect(bodyGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -170), new Vector2(-24, -72));
            _modalBody = AddText(bodyGO, "", 19, Color.white, TextAnchor.UpperLeft);

            // 内容区：横向排列卡牌视图或文字按钮，与手牌区同一套HLG布局（不再用ScrollRect/Mask/CSF链）
            var btnsGO = UIObj("Buttons", panel.transform);
            SetRect(btnsGO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 20), new Vector2(-24, 180));
            var hlg = btnsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            _modalBtns = (RectTransform)btnsGO.transform;

            _modal.SetActive(false);
        }

        // ==================================================================
        // 刷新（引擎状态 → 界面）
        // ==================================================================

        public void Refresh()
        {
            if (_e == null) return;
            _legal = (_e.GameOver || _e.Pending != null)
                ? new List<GameAction>()
                : _e.GetLegalActions(_me);

            var cur = _e.Players[_e.Current];
            _turnText.text = _e.GameOver
                ? "对 局 结 束"
                : ("第 " + _e.TurnNumber + " 轮 · " + (cur.IsHuman ? "你的回合" : cur.Name + " 的回合"));

            int held = _e.HeldCountOf(_me);
            _infoText.text = "弃牌堆: " + _e.Discard.Count
                + "    未发牌堆: " + _e.SideDeck.Count
                + "    我的工人: " + _e.Players[_me].WorkerCount
                + (held > 0 ? "    被扣押: " + held : "");

            RebuildOpponents();
            RebuildHand();

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me && _e.Players[_me].Alive;
            _attackBtn.interactable = myTurn && _legal.Any(a => a.Kind == ActionKind.Attack);
            _endBtn.interactable = myTurn;
            _cancelBtn.gameObject.SetActive(_targeting != null && myTurn);

            UpdateHint(myTurn);
            RefreshLog();

            // 抉择：操作栏按钮（主通道）+ 弹窗（辅助展示），两者调用同一提交接口
            RebuildPendingButtons();

            if (_e.GameOver)
            {
                if (!_gameOverShown) { _gameOverShown = true; ShowGameOverModal(); }
            }
            else if (_e.Pending != null && _e.Pending.Seat == _me)
            {
                // 签名未变则不重建弹窗，避免每帧重建按钮导致点击丢失
                if (PendingSig(_e.Pending) != _modalSig) { _modalSig = PendingSig(_e.Pending); ShowPendingModal(_e.Pending); }
            }
            else
            {
                _modalSig = null;
                if (!_modalSticky) HideModal();
            }
        }

        string _modalSig;

        static string PendingSig(PendingChoice pc)
        {
            if (pc == null) return null;
            return pc.Kind + ":" + pc.Seat + ":"
                + (pc.DrawnCard != null ? pc.DrawnCard.Id : 0) + ":"
                + (pc.Options != null ? pc.Options.Count : 0);
        }

        // 把抉择直接渲染成操作栏按钮——这是界面上已被验证可靠的区域，
        // 即使弹窗因任何原因没显示，玩家也永远能在这里作出选择。
        void RebuildPendingButtons()
        {
            var pc = (_e.Pending != null && _e.Pending.Seat == _me) ? _e.Pending : null;
            string sig = PendingSig(pc);
            if (sig == _pendingSig) return;   // 签名未变，不动现有按钮
            _pendingSig = sig;

            foreach (var go in _pendingBtns) Destroy(go);
            _pendingBtns.Clear();
            if (pc == null) return;

            if (pc.Kind == ChoiceKind.GuardIntercept)
            {
                AddPendingButton("用侍卫挡刀（保住【" + pc.DrawnCard.Type.Name() + "】）",
                    () => { _e.SubmitGuardChoice(true); Refresh(); },
                    new Color(0.8f, 0.55f, 0.15f));
                AddPendingButton("不挡（【" + pc.DrawnCard.Type.Name() + "】进弃牌堆）",
                    () => { _e.SubmitGuardChoice(false); Refresh(); },
                    new Color(0.5f, 0.5f, 0.55f));
            }
            else
            {
                // 弃牌堆选牌可能很多，操作栏最多放4个，完整列表在弹窗里
                foreach (var c in pc.Options.Take(4))
                {
                    var card = c;
                    AddPendingButton(pc.Reason + "：拿回【" + card.Type.Name() + "】",
                        () => { _e.SubmitDiscardPick(card.Id); Refresh(); },
                        new Color(0.3f, 0.55f, 0.85f));
                }
            }
        }

        void AddPendingButton(string label, UnityAction act, Color color)
        {
            var btn = MakeButton(_actionBar, label, act, color, 18);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            // 插到"结束回合"之后、提示文字之前
            btn.transform.SetSiblingIndex(2 + _pendingBtns.Count);
            _pendingBtns.Add(btn.gameObject);
        }

        void UpdateHint(bool myTurn)
        {
            if (_e.GameOver) { _hintText.text = ""; return; }
            if (_e.Pending != null) { _hintText.text = "请在弹窗中作出抉择。"; return; }
            if (!myTurn) { _hintText.text = "对手行动中……"; return; }
            if (_targeting != null) { _hintText.text = "点击上方一名对手作为目标。"; return; }
            _hintText.text =
                "轮到你行动：\n" +
                "· 点【攻击】消耗本回合攻击机会\n" +
                "· 点亮的角色牌可点击发动技能\n" +
                (_e.BasicAttackUsed ? "（本回合基础攻击已用）\n" : "") +
                "· 行动完毕后点【结束回合】";
        }

        void RefreshLog()
        {
            int start = Mathf.Max(0, _log.Count - LogShow);
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < _log.Count; i++)
            {
                sb.AppendLine(_log[i]);
            }
            _logText.text = sb.ToString();
        }

        void OnEngineLog(string msg)
        {
            _log.Add(msg);
            if (_log.Count > LogKeep) _log.RemoveRange(0, _log.Count - LogKeep);

            // 关键动作显示到中央横幅（回合标题除外）
            if (_banner != null && !msg.StartsWith("——") && !msg.StartsWith("======"))
            {
                _banner.text = msg;
                _bannerAlpha = 1f;
                var c = _banner.color;
                c.a = 1f;
                _banner.color = c;
            }
        }

        void Update()
        {
            // 横幅淡出
            if (_banner != null && _bannerAlpha > 0f)
            {
                _bannerAlpha -= Time.deltaTime * 0.7f;
                var c = _banner.color;
                c.a = Mathf.Clamp01(_bannerAlpha);
                _banner.color = c;
            }

            // Esc 同样唤起退出确认
            if (Input.GetKeyDown(KeyCode.Escape)) OnQuitClicked();
        }

        void OnQuitClicked()
        {
            // Application.Quit 只在打包后的exe里生效，编辑器Play模式下点了没反应（正常现象）
            _modalSticky = true;   // 防止AI行动时的自动刷新把确认弹窗收掉
            ShowModalBase("退出游戏", "确定要退出吗？当前对局不会保存。");
            AddModalTextButton("退出", () => Application.Quit());
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        bool _modalSticky;

        // ==================================================================
        // 对手面板
        // ==================================================================

        void RebuildOpponents()
        {
            foreach (Transform child in _oppArea) Destroy(child.gameObject);

            foreach (var p in _e.Players)
            {
                if (p.Seat == _me) continue;

                bool isTarget = _targeting != null && _targeting.Any(a => a.TargetSeat == p.Seat);
                var panel = UIObj("Opp_" + p.Seat, _oppArea);
                Color bgc = !p.Alive ? new Color(0.25f, 0.22f, 0.22f)
                          : isTarget ? new Color(0.75f, 0.6f, 0.15f)
                          : new Color(0.22f, 0.26f, 0.34f);
                AddImage(panel, bgc);
                var le = panel.AddComponent<LayoutElement>();
                le.preferredWidth = 280;

                var nameGO = UIObj("Name", panel.transform);
                SetRect(nameGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -48), new Vector2(-12, -8));
                AddText(nameGO, p.Name + (p.Alive ? "" : "（已出局）"), 22,
                    p.Alive ? Color.white : new Color(0.6f, 0.55f, 0.55f), TextAnchor.MiddleCenter);

                var cardsGO = UIObj("Cards", panel.transform);
                SetRect(cardsGO, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 12), new Vector2(-12, -52));
                string info = p.Alive
                    ? "手牌: " + p.Hand.Count + " 张" +
                      (_e.HeldCountOf(p.Seat) > 0 ? "\n被扣押: " + _e.HeldCountOf(p.Seat) + " 张" : "") +
                      (isTarget ? "\n▼ 点击选为目标 ▼" : "")
                    : "☠";
                AddText(cardsGO, info, 18,
                    p.Alive ? new Color(0.8f, 0.85f, 0.9f) : new Color(0.6f, 0.4f, 0.4f), TextAnchor.MiddleCenter);

                if (isTarget)
                {
                    int seat = p.Seat;
                    var btn = panel.AddComponent<Button>();
                    btn.onClick.AddListener(() => OnTargetPicked(seat));
                }
            }
        }

        // ==================================================================
        // 手牌区
        // ==================================================================

        static readonly CardType[] DisplayOrder =
        {
            CardType.Arsenal, CardType.Exorcist, CardType.Police, CardType.Priest,
            CardType.Guard, CardType.Witch, CardType.Undead, CardType.Worker
        };

        void RebuildHand()
        {
            foreach (Transform child in _handArea) Destroy(child.gameObject);

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me;
            var sorted = _e.Players[_me].Hand
                .OrderBy(c => System.Array.IndexOf(DisplayOrder, c.Type))
                .ThenBy(c => c.Id);

            foreach (var c in sorted)
            {
                bool usable = myTurn && _legal.Any(a => a.SourceCardId == c.Id);
                var card = c;
                MakeCardView(card, _handArea, SubLabel(card), usable, myTurn, 128,
                    () => OnCardClicked(card));
            }
        }

        /// <summary>
        /// 创建一张卡牌视图：底色Image + 子物体文字 + 可点击Button。
        /// 手牌区和弹窗选牌共用同一套视觉（一个GameObject只能有一个Graphic，文字必须在子物体上）。
        /// highlighted=是否提亮（手牌中可发动的牌）；interactable=可否点击；width=卡牌宽度。
        /// </summary>
        Button MakeCardView(Card c, Transform parent, string caption, bool highlighted, bool interactable, float width, UnityAction onClick)
        {
            var go = UIObj("Card_" + c.Id, parent);
            AddImage(go, highlighted ? Lighten(ColorOf(c.Type), 1.25f) : ColorOf(c.Type));
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            AddText(txtGO, c.Type.Name() + (string.IsNullOrEmpty(caption) ? "" : "\n" + caption),
                20, TextColorOf(c.Type), TextAnchor.MiddleCenter);

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.highlightedColor = Color.white;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        string SubLabel(Card c)
        {
            switch (c.Type)
            {
                case CardType.Worker:   return "存活之本";
                case CardType.Undead:   return "免疫攻击";
                case CardType.Exorcist: return c.UsedSkillThisTurn ? "本回合已用" : "可捉亡灵";
                case CardType.Police:   return c.UsedSkillThisTurn ? "本回合已用" : "可扣押沉默";
                case CardType.Priest:   return "亡语:回收";
                case CardType.Guard:    return "可挡一刀";
                case CardType.Arsenal:  return c.UsedSkillThisTurn ? "本回合已用" : "可追加攻击";
                case CardType.Witch:
                    return (c.PoisonUsed ? "毒✗" : "毒✓") + " " + (c.AntidoteUsed ? "药✗" : "药✓");
                default: return "";
            }
        }

        static Color ColorOf(CardType t)
        {
            switch (t)
            {
                case CardType.Worker:   return new Color(0.55f, 0.55f, 0.58f);
                case CardType.Undead:   return new Color(0.32f, 0.2f, 0.5f);
                case CardType.Exorcist: return new Color(0.9f, 0.75f, 0.25f);
                case CardType.Police:   return new Color(0.22f, 0.5f, 0.85f);
                case CardType.Priest:   return new Color(0.92f, 0.9f, 0.78f);
                case CardType.Guard:    return new Color(0.5f, 0.36f, 0.26f);
                case CardType.Witch:    return new Color(0.6f, 0.28f, 0.68f);
                case CardType.Arsenal:  return new Color(0.85f, 0.28f, 0.22f);
                default:                return Color.gray;
            }
        }

        static Color TextColorOf(CardType t)
        {
            // 浅色底配深色字，深色底配白字
            switch (t)
            {
                case CardType.Worker:
                case CardType.Exorcist:
                case CardType.Priest:
                    return new Color(0.15f, 0.15f, 0.18f);
                default:
                    return Color.white;
            }
        }

        static Color Lighten(Color c, float k)
        {
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }

        // ==================================================================
        // 玩家输入
        // ==================================================================

        void OnAttackClicked()
        {
            var acts = _legal.Where(a => a.Kind == ActionKind.Attack).ToList();
            if (acts.Count == 0) return;
            _targeting = acts;
            Refresh();
        }

        void OnCancelTargetClicked()
        {
            _targeting = null;
            Refresh();
        }

        void OnEndTurnClicked()
        {
            _targeting = null;
            _e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
            Refresh();
        }

        void OnTargetPicked(int seat)
        {
            if (_targeting == null) return;
            var act = _targeting.FirstOrDefault(a => a.TargetSeat == seat);
            _targeting = null;
            if (act == null) { Refresh(); return; }
            _e.ApplyAction(act);
            Refresh();
        }

        void OnCardClicked(Card c)
        {
            if (_e.GameOver || _e.Pending != null || _e.Current != _me) return;

            var acts = _legal.Where(a => a.SourceCardId == c.Id).ToList();
            if (acts.Count == 0)
            {
                // 被动/无可用技能的牌：给一句说明
                switch (c.Type)
                {
                    case CardType.Worker:   SetTempHint("【工人】维持你的存活，失去所有工人即出局。"); break;
                    case CardType.Undead:   SetTempHint("【亡灵】是被动牌：被攻击抽中时自动返回手牌。"); break;
                    case CardType.Priest:   SetTempHint("【牧师】亡语：被抽走进入弃牌堆时，自动触发回收。"); break;
                    case CardType.Guard:    SetTempHint("【侍卫】是被动牌：被攻击时会询问你是否挡刀。"); break;
                    default:                SetTempHint("该牌本回合已行动过，或暂无可用技能。"); break;
                }
                return;
            }

            var kinds = acts.Select(a => a.Kind).Distinct().ToList();
            if (kinds.Count == 1 && kinds[0] == ActionKind.Antidote)
            {
                _e.ApplyAction(acts[0]);
                Refresh();
                return;
            }
            if (kinds.Count == 1)
            {
                _targeting = acts;
                Refresh();
                return;
            }

            // 女巫：毒药 / 解药二选一
            var poison = acts.Where(a => a.Kind == ActionKind.PoisonAttack).ToList();
            var antidote = acts.FirstOrDefault(a => a.Kind == ActionKind.Antidote);
            ShowModalBase("女巫的药剂", "选择本张【女巫】要使用的药剂（毒药与解药整局各限一次）：");
            if (poison.Count > 0)
                AddModalTextButton("毒药（额外攻击一次）", () => { _targeting = poison; Refresh(); });
            if (antidote != null)
                AddModalTextButton("解药（从弃牌堆回收一张角色牌）", () => { _e.ApplyAction(antidote); Refresh(); });
            AddModalTextButton("取消", () => Refresh());
        }

        void SetTempHint(string s)
        {
            _hintText.text = s;
        }

        // ==================================================================
        // 弹窗
        // ==================================================================

        void HideModal()
        {
            if (_modal.activeSelf) _modal.SetActive(false);
        }

        void ShowPendingModal(PendingChoice pc)
        {
            if (pc.Kind == ChoiceKind.GuardIntercept)
            {
                // 侍卫挡刀：两张牌二选一。点被抽中的牌=不挡；点侍卫=挡刀
                var attacker = _e.Players[pc.AttackerSeat];
                var guard = _e.Players[_me].FirstOf(CardType.Guard);
                ShowModalBase("侍卫挡刀？",
                    attacker.Name + " 的攻击抽中了你的【" + pc.DrawnCard.Type.Name() + "】！\n点选一张来决定：");
                AddModalCard(pc.DrawnCard, "被抽中了\n点我 = 不挡",
                    () => { _e.SubmitGuardChoice(false); Refresh(); });
                if (guard != null)
                    AddModalCard(guard, "点我 = 挡刀\n侍卫代替进弃牌堆",
                        () => { _e.SubmitGuardChoice(true); Refresh(); });
            }
            else // PickFromDiscard（牧师亡语 / 女巫解药）：每种角色牌只展示一张，点哪张拿哪张
            {
                ShowModalBase(pc.Reason, "从弃牌堆点选一张角色牌拿回手牌：");
                foreach (var c in pc.Options.GroupBy(o => o.Type).Select(g => g.First()))
                {
                    var card = c;
                    AddModalCard(card, "", () => { _e.SubmitDiscardPick(card.Id); Refresh(); });
                }
            }
        }

        void ShowGameOverModal()
        {
            bool win = _e.WinnerSeat == _me;
            ShowModalBase(
                win ? "你赢了！" : "对局结束",
                (win ? "你是最后的存活者！" : "胜者：" + _e.Players[_e.WinnerSeat].Name) +
                "\n共进行了 " + _e.TurnNumber + " 轮。");
            AddModalTextButton("再来一局", () => { _targeting = null; _boot.Restart(); });
            AddModalTextButton("返回主菜单", () => { _targeting = null; _boot.BackToMenu(); });
        }
    }
}
