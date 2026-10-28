using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 运行时代码生成的游戏界面（uGUI），无需任何场景预制。
    /// 布局：顶部信息栏 / 对手区 / 对局日志 / 手牌区+操作栏 / 中央抉择弹窗。
    ///
    /// 2026-09 重构：
    ///   搭积木方法（UIObj/SetRect/AddImage/AddText/MakeButton/Lighten/字体/事件系统）
    ///   已抽到 UIKit，本类只保留"初代界面特有"的布局与交互逻辑。
    ///   卡牌名称/副标签/说明文字来自 Core 的 CardDefs 表，颜色来自 CardVisual 表。
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

        GameObject _help;                  // 规则帮助浮层（M 键开关）
        Text _helpTitle, _helpText;
        int _helpPage;
        GameObject _helpNav;               // 帮助菜单的翻页按钮组（复盘模式下隐藏）
        bool _helpReview;                  // true = 浮层正在显示对局复盘（终局后不被自动关闭）
        RectTransform _helpContent;        // 滚动内容容器（滚轮定位目标）
        InputField _helpInput;             // 2026-10-14：只读输入框承载文本 → 可拖选、Ctrl+C 复制
        Button _copyBtn;                   // 复盘模式"复制全部"按钮（一键导出整份日志）

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
            var btn = UIKit.MakeButton(_modalBtns, label, act, new Color(0.3f, 0.45f, 0.7f), 20, _font);
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
            _font = UIKit.LoadFont();

            _log.AddRange(_e.History);
            _e.OnLog += OnEngineLog;

            UIKit.EnsureEventSystem();
            BuildCanvas();
            Refresh();
        }

        GameObject UIObj(string name, Transform parent) { return UIKit.UIObj(name, parent); }
        void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        { UIKit.SetRect(go, aMin, aMax, offMin, offMax); }
        Image AddImage(GameObject go, Color c) { return UIKit.AddImage(go, c); }
        Text AddText(GameObject go, string content, int size, Color color, TextAnchor anchor)
        { return UIKit.AddText(go, content, size, color, anchor, _font); }
        Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int fontSize = 20)
        { return UIKit.MakeButton(parent, label, onClick, bg, fontSize, _font); }

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

            // 手牌区（底部）：保持原始布局；手牌>9张时只缩牌宽（RebuildHand），不动比例
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
            BuildHelp(root);
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
        // 规则帮助浮层（M 键）：第1页胜利规则，第2页卡牌一览（可滚动）；终局兼作复盘
        // ==================================================================

        void BuildHelp(RectTransform root)
        {
            _help = UIObj("Help", root);
            SetRect(_help, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(_help, new Color(0, 0, 0, 0.7f));

            var panel = UIObj("Panel", _help.transform);
            SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-430, -310), new Vector2(430, 310));
            AddImage(panel, new Color(0.16f, 0.18f, 0.23f));

            var titleGO = UIObj("Title", panel.transform);
            SetRect(titleGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -58), new Vector2(-20, -10));
            _helpTitle = AddText(titleGO, "", 26, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter);

            // 滚动区：ScrollRect + Mask 视口 + ContentSizeFitter 内容（滚轮从上往下翻）
            var scrollGO = UIObj("Scroll", panel.transform);
            SetRect(scrollGO, Vector2.zero, Vector2.one, new Vector2(24, 84), new Vector2(-24, -70));

            var viewport = UIObj("Viewport", scrollGO.transform);
            SetRect(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(viewport, new Color(0.12f, 0.13f, 0.17f));
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var content = UIObj("Content", viewport.transform);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(14, 0);
            crt.offsetMax = new Vector2(-14, 0);
            _helpContent = crt;
            _helpText = AddText(content, "", 19, Color.white, TextAnchor.UpperLeft);
            _helpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _helpText.verticalOverflow = VerticalWrapMode.Overflow;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // 2026-10-17：可选取/复制 = 只读 InputField 直接加在内容容器上（textComponent=同物体的Text）。
            // 注意必须保持"Text 挂在容器上 + ContentSizeFitter"的原版结构——垂直布局方案会把 Text 压成一行。
            var input = content.AddComponent<InputField>();
            input.textComponent = _helpText;
            input.readOnly = true;
            input.lineType = InputField.LineType.MultiLineNewline;
            input.shouldHideMobileInput = true;
            input.customCaretColor = true;
            input.caretColor = new Color(1f, 1f, 1f, 0.9f);
            input.selectionColor = new Color(0.3f, 0.55f, 0.95f, 0.35f);
            _helpInput = input;

            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            // 底栏按钮：翻页 + 关闭
            var btns = UIObj("Buttons", panel.transform);
            SetRect(btns, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 16), new Vector2(-24, 72));
            var hlg = btns.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            _helpNav = UIObj("Nav", btns.transform);
            var navHlg = _helpNav.AddComponent<HorizontalLayoutGroup>();
            navHlg.spacing = 14;
            navHlg.childForceExpandWidth = false;
            navHlg.childForceExpandHeight = true;
            navHlg.childAlignment = TextAnchor.MiddleCenter;
            MakeButton(_helpNav.transform, "上一页", () => ShowHelpPage(_helpPage - 1),
                new Color(0.3f, 0.45f, 0.7f), 20).gameObject.AddComponent<LayoutElement>().preferredWidth = 200;
            MakeButton(_helpNav.transform, "下一页", () => ShowHelpPage(_helpPage + 1),
                new Color(0.3f, 0.45f, 0.7f), 20).gameObject.AddComponent<LayoutElement>().preferredWidth = 200;
            // 复盘模式专用：一键把整份日志复制到系统剪贴板（2026-10-14，与2代同步）
            _copyBtn = MakeButton(btns.transform, "复制全部", OnCopyReview,
                new Color(0.2f, 0.55f, 0.4f), 20);
            _copyBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 200;
            _copyBtn.gameObject.SetActive(false);
            MakeButton(btns.transform, "关闭（M）", CloseHelp,
                new Color(0.55f, 0.3f, 0.3f), 20).gameObject.AddComponent<LayoutElement>().preferredWidth = 200;

            _help.SetActive(false);
        }

        void OpenHelp() { ShowHelpPage(0); _help.SetActive(true); }
        void CloseHelp()
        {
            bool wasReview = _helpReview;
            _helpReview = false;
            _help.SetActive(false);
            // 复盘看完把终局面板叫回来（复盘打开时 HideModal 过）
            if (wasReview && _e != null && _e.GameOver && !_modal.activeSelf)
                ShowGameOverModal();
        }

        /// <summary>终局复盘：完整对局日志（引擎 History 全量），滚轮从上往下翻。开头附赢家剩余手牌构成（2026-10-20）。</summary>
        void OpenReview()
        {
            _helpReview = true;
            _helpTitle.text = "对局复盘（共 " + _e.History.Count + " 条记录）";
            var sb = new System.Text.StringBuilder();
            // 终局信息：赢家剩余手牌构成（2026-10-20 用户要求，与2代同步）
            int wSeat = _e.WinnerSeat;
            if (wSeat >= 0)
            {
                var wh = _e.Players[wSeat].Hand;
                sb.Append("【赢家手牌】").Append(_e.Players[wSeat].Name)
                  .Append(" 终局剩余 ").Append(wh.Count).Append(" 张：");
                var groups = wh.GroupBy(c => c.Type).OrderByDescending(g => g.Count());
                sb.Append(wh.Count == 0 ? "（空手）" : string.Join(" ",
                    groups.Select(g => (g.Key == CardType.Hero ? _e.Players[wSeat].HeroName : g.Key.Name()) + "×" + g.Count())));
                sb.Append("\n\n────────\n\n");
            }
            sb.Append(string.Join("\n", _e.History.ToArray()));
            SetHelpText(sb.ToString());
            if (_helpNav != null) _helpNav.SetActive(false);
            if (_copyBtn != null) _copyBtn.gameObject.SetActive(true);
            if (_helpContent != null)
                _helpContent.anchoredPosition = Vector2.zero;   // 滚回顶部
            _help.SetActive(true);
        }

        // 文本必须经 InputField.text 写入（它内部缓存一份，Ctrl+C 复制的是缓存；直接改 Text 会复制到旧内容）
        void SetHelpText(string s)
        {
            if (_helpInput != null) _helpInput.text = s;
            else if (_helpText != null) _helpText.text = s;
        }

        // 复盘模式：整份日志一键进系统剪贴板（2026-10-14 用户要求：导出复盘）
        void OnCopyReview()
        {
            GUIUtility.systemCopyBuffer = _helpInput != null ? _helpInput.text : (_helpText != null ? _helpText.text : "");
            var label = _copyBtn != null ? _copyBtn.GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                label.text = "已复制到剪贴板！";
                StartCoroutine(CopyFeedback(label));
            }
        }

        System.Collections.IEnumerator CopyFeedback(Text label)
        {
            yield return new WaitForSeconds(1.5f);
            if (label != null) label.text = "复制全部";
        }

        void ShowHelpPage(int page)
        {
            _helpReview = false;
            if (_helpNav != null) _helpNav.SetActive(true);
            if (_copyBtn != null) _copyBtn.gameObject.SetActive(false);
            _helpPage = (page % 2 + 2) % 2;   // 只有两页，循环翻
            _helpTitle.text = _helpPage == 0 ? "规则帮助（1/2）· 怎么获胜" : "规则帮助（2/2）· 卡牌一览";
            SetHelpText(_helpPage == 0 ? VictoryPageText() : CardsPageText());
            // 换页后滚回顶部
            if (_helpContent != null)
                _helpContent.anchoredPosition = Vector2.zero;
        }

        string VictoryPageText()
        {
            return
                "【胜利条件】\n" +
                "清空所有对手的工人——工人是各自的生命之源，成为最后存活的玩家即获胜。\n\n" +
                "【回合规则】\n" +
                "· 基础攻击每回合1次：随机抽走对手1张手牌，置入弃牌堆。\n" +
                "· 开局每人 9 张随机牌 ＋ 1 张保底工人；本版本无补牌，牌越打越少，工人消耗不可恢复。\n\n" +
                "【快捷键】\n" +
                "空格 = 结束回合　　M = 规则帮助　　Esc = 退出游戏";
        }

        /// <summary>卡牌一览：名字和数量读 CardDefs 表与牌库构成；效果文案按 1 代规则手写（CardDefs 是 2 代口径）。</summary>
        string CardsPageText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【牌库构成】");
            foreach (var kv in _boot.DeckSpec())
            {
                sb.AppendLine("【" + CardDefs.Of(kv.Key).Name + "】×" + kv.Value);
                sb.AppendLine(FirstGenDesc(kv.Key));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string FirstGenDesc(CardType t)
        {
            switch (t)
            {
                case CardType.Worker:   return "生命之源，失去所有工人即出局。";
                case CardType.Arsenal:    return "主动：每回合可额外攻击一次。";
                case CardType.Witch:      return "主动：毒药＝额外攻击一次；解药＝从弃牌堆回收一张角色牌。整局各限一次。";
                case CardType.Undead:     return "被动：免疫攻击，被攻击抽中时自动返回手牌；驱魔师可将其处决。";
                case CardType.Exorcist:   return "主动（每回合限1次）：抽对手一张牌验明正身，是亡灵则当场处决，否则归还。";
                case CardType.Priest:     return "被动（亡语）：被抽走进入弃牌堆时，从弃牌堆回收一张角色牌。";
                case CardType.Police:     return "主动（每回合限1次）：从对手手牌抽一张牌扣押，被扣押期间不可用，目标下回合结束后归还。";
                case CardType.Guard:      return "被动：被攻击时可挺身而出，代替被抽中的牌进入弃牌堆。";
                default:                  return "";
            }
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
                // 结算弹窗优先，帮助浮层让位；但复盘模式下保持打开
                if (!_helpReview) CloseHelp();
                if (!_gameOverShown) { _gameOverShown = true; ShowGameOverModal(); }
            }
            else if (_e.Pending != null && _e.Pending.Seat == _me)
            {
                CloseHelp();   // 挡刀/亡语选牌等抉择弹窗优先，帮助浮层让位
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
                "· 行动完毕后【结束回合】或按 空格\n" +
                "· M 随时查看规则帮助";
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

            // 帮助浮层打开时，空格/M/Esc 都归浮层管，不触发回合操作
            bool helpOpen = _help != null && _help.activeSelf;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (helpOpen) CloseHelp();
                else OnQuitClicked();
            }
            if (helpOpen)
            {
                if (Input.GetKeyDown(KeyCode.M)) CloseHelp();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.M)) OpenHelp();
                // 空格 = 结束回合：与【结束回合】按钮同一门槛（自己回合、无弹窗、无待决）
                if (Input.GetKeyDown(KeyCode.Space)
                    && _e != null && !_e.GameOver && _e.Pending == null
                    && _e.Current == _me && _e.Players[_me].Alive
                    && !_modal.activeSelf)
                    OnEndTurnClicked();
            }
        }

        void OnQuitClicked()
        {
            _modalSticky = true;   // 防止AI行动时的自动刷新把确认弹窗收掉
            ShowModalBase("退出游戏", "确定要退出吗？当前对局不会保存。");
            AddModalTextButton("退出", () =>
            {
                Application.Quit();
#if UNITY_EDITOR
                // Application.Quit 在编辑器里无效——直接停掉 Play 模式，不然看起来"点了没反应"
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
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
            var def = CardVisual.Of(c.Type);
            AddImage(go, highlighted ? UIKit.Lighten(def.Bg, 1.25f) : def.Bg);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            // 符号图标：有图标的牌型图标置顶，文字收窄到底部；其余维持原状
            var icon = CardVisual.IconOf(c.Type);
            bool hasIcon = icon != null;
            if (hasIcon)
            {
                var iconGO = UIObj("Icon", go.transform);
                SetRect(iconGO, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(-30, -width * 0.52f), new Vector2(30, -6));
                var img = iconGO.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
            }

            var txtGO = UIObj("Label", go.transform);
            if (hasIcon)
                SetRect(txtGO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(4, 4), new Vector2(-4, width * 0.5f));
            else
                SetRect(txtGO, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            string name = c.Type == CardType.Hero ? _e.Players[_me].HeroName : CardDefs.Of(c.Type).Name;
            AddText(txtGO, name + (string.IsNullOrEmpty(caption) ? "" : "\n" + caption),
                20, def.Text, TextAnchor.MiddleCenter);

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.highlightedColor = Color.white;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>手牌副标签：查 CardDefs 表拿静态文案，动态状态（已用/毒✓药✗）在这里拼接。</summary>
        string SubLabel(Card c)
        {
            if (c.Type == CardType.Witch)
                return (c.PoisonUsed ? "毒✗" : "毒✓") + " " + (c.AntidoteUsed ? "药✗" : "药✓");
            var def = CardDefs.Of(c.Type);
            if (def.HasActiveSkill)
                return c.UsedSkillThisTurn ? "本回合已用" : def.SkillTag;
            return def.PassiveTag;
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
                // 被动/无可用技能的牌：给一句说明（文案来自 CardDefs 表）
                var def = CardDefs.Of(c.Type);
                if (def.HasActiveSkill || !string.IsNullOrEmpty(def.PassiveDesc))
                    SetTempHint("【" + def.Name + "】" + (def.HasActiveSkill ? def.SkillDesc : def.PassiveDesc));
                else
                    SetTempHint("该牌本回合已行动过，或暂无可用技能。");
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
            AddModalTextButton("复盘", () => { _modalSticky = false; HideModal(); OpenReview(); });
            AddModalTextButton("再来一局", () => { _targeting = null; _boot.Restart(); });
            AddModalTextButton("返回主菜单", () => { _targeting = null; _boot.BackToMenu(); });
        }
    }
}
