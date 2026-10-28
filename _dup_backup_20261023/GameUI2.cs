using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2代对局界面。布局骨架与初代同款：
    /// HLG childForceExpandHeight=true + 子元素只给 LayoutElement.preferredWidth、
    /// 操作栏按钮只给 preferredHeight、Update 不做每帧 Refresh（由 Bootstrap 循环和玩家动作驱动）、
    /// 抉择以操作栏按钮为主通道、弹窗为辅助展示。
    /// 2代新增：HP/MP与英雄显示、英雄技按钮、守卫护牌/军机大臣勒索/女巫弹窗、亡语反击按钮。
    ///
    /// 2026-09 重构：搭积木方法同初代一起抽到 UIKit；卡牌文案/颜色来自 CardDefs + CardVisual 表。
    /// </summary>
    public class GameUI2 : MonoBehaviour
    {
        GameEngine2 _e;
        GameEngine3 _g3;   // 3代「法术迸发」引擎（2026-10-21）：非 null 时启用法术相关 UI
        IGameSession _boot;   // 2代/3代会话通吃（GameBootstrap2 / GameBootstrap3）
        Font _font;
        const int _me = 0;

        Text _turnText, _infoText, _logText, _hintText, _banner;
        float _bannerAlpha;
        RectTransform _oppArea, _handArea;
        Transform _actionBar;
        Button _craftBtn, _attackBtn, _endBtn, _cancelBtn;
        Button _deathblowBtn, _chivalryBtn;   // 致命一击 / 骑士精神·追击（2026-10-14 英雄技触发入口）
        Button _learnBtn;                     // 3代：研习（learn）入口，消耗技能点点亮法术释放权限
        Button _mulliganConfirmBtn, _mulliganSkipBtn;   // 开局调度（2026-10-20）：右侧确认/跳过
        readonly HashSet<int> _mulliganPicks = new HashSet<int>();   // 已选中待调度的手牌 Id
        bool _mulliganWasMine;                                       // 上升沿检测：入场时闪一次横幅

        GameObject _modal;
        Text _modalTitle, _modalBody;
        RectTransform _modalBtns, _modalBtns2;
        string _modalSig, _pendingSig;
        bool _modalSticky, _gameOverShown;
        GameObject _help;                  // 规则帮助浮层（M 键开关）
        Text _helpTitle, _helpText;
        int _helpPage;
        GameObject _helpNav;               // 帮助菜单的翻页按钮组（复盘模式下隐藏）
        bool _helpReview;                  // true = 浮层正在显示对局复盘（终局后不被自动关闭）
        RectTransform _helpContent;        // 滚动内容容器（滚轮定位目标）
        InputField _helpInput;             // 2026-10-14：只读输入框承载文本 → 可拖选、Ctrl+C 复制
        Button _copyBtn;                   // 复盘模式"复制全部"按钮（一键导出整份日志）
        // 手牌区/对手面板的重建签名：状态没变就不重建。
        // 点击需要"按下与松开发送给同一对象"，反复 Destroy 重建会让点击永远不成。
        string _handSig, _oppSig;

        readonly List<string> _log = new List<string>();
        List<GameAction> _legal = new List<GameAction>();
        List<GameAction> _targeting;   // 非null = 正在选目标（攻击/技能/贿赂选对象）
        readonly List<GameObject> _pendingBtns = new List<GameObject>();

        const int LogKeep = 300, LogShow = 14;

        // ==================================================================
        // 初始化与搭建（布局与初代完全一致）
        // ==================================================================

        public void Init(GameEngine2 e, IGameSession boot)
        {
            _e = e;
            _g3 = e as GameEngine3;   // 3代对局：启用法术 UI（研习/施放/标记/反制）
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
            var canvasGO = new GameObject("SandKillCanvas2", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGO.transform;

            var bg = UIObj("Background", root);
            SetRect(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(bg, new Color(0.11f, 0.1f, 0.16f));

            // 顶部信息栏
            var top = UIObj("TopBar", root);
            SetRect(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), Vector2.zero);
            AddImage(top, new Color(0.16f, 0.16f, 0.24f));

            var titleGO = UIObj("Title", top.transform);
            SetRect(titleGO, new Vector2(0, 0), new Vector2(0.2f, 1), new Vector2(16, 0), Vector2.zero);
            AddText(titleGO, "3D杀·二代", 26, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleLeft);
            if (_g3 != null)
            {
                var t = titleGO.GetComponentInChildren<Text>();
                if (t != null) { t.text = "3D杀·三代·法术迸发"; t.color = new Color(0.6f, 0.85f, 0.95f); }
            }

            var turnGO = UIObj("TurnText", top.transform);
            SetRect(turnGO, new Vector2(0.2f, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero);
            _turnText = AddText(turnGO, "", 24, Color.white, TextAnchor.MiddleCenter);

            var infoGO = UIObj("InfoText", top.transform);
            SetRect(infoGO, new Vector2(0.55f, 0), new Vector2(0.9f, 1), Vector2.zero, Vector2.zero);
            _infoText = AddText(infoGO, "", 17, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight);

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

            // 日志区（中部）：纯文本，无ScrollRect/Mask/CSF
            var logGO = UIObj("LogArea", root);
            SetRect(logGO, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 260), new Vector2(-16, -308));
            AddImage(logGO, new Color(0.07f, 0.08f, 0.1f));
            var logTextGO = UIObj("LogText", logGO.transform);
            SetRect(logTextGO, Vector2.zero, Vector2.one, new Vector2(12, 10), new Vector2(-12, -10));
            _logText = AddText(logTextGO, "", 18, new Color(0.85f, 0.88f, 0.92f), TextAnchor.UpperLeft);
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;

            // 手牌区（底部）：保持原始高度；手牌>9张时只缩牌宽（RebuildHand），不动布局比例
            var handGO = UIObj("HandArea", root);
            SetRect(handGO, new Vector2(0, 0), new Vector2(0.82f, 0), new Vector2(16, 16), new Vector2(-8, 236));
            AddImage(handGO, new Color(0.16f, 0.17f, 0.24f));
            _handArea = (RectTransform)handGO.transform;
            var handHlg = handGO.AddComponent<HorizontalLayoutGroup>();
            handHlg.spacing = 10;
            handHlg.padding = new RectOffset(10, 10, 10, 10);
            handHlg.childForceExpandWidth = false;
            handHlg.childForceExpandHeight = true;
            handHlg.childAlignment = TextAnchor.MiddleLeft;

            // 操作栏（右下）：保持原始高度与按钮尺寸；按钮太多排不下时由 FitActionBar 按需等比压缩
            var barGO = UIObj("ActionBar", root);
            SetRect(barGO, new Vector2(0.82f, 0), new Vector2(1, 0), new Vector2(8, 16), new Vector2(-16, 236));
            var barVlg = barGO.AddComponent<VerticalLayoutGroup>();
            barVlg.spacing = 10;
            barVlg.childForceExpandWidth = true;
            barVlg.childForceExpandHeight = false;
            barVlg.childAlignment = TextAnchor.UpperCenter;
            _actionBar = barGO.transform;

            // 合成按钮（2026-10-06 起英雄技改由点击英雄牌触发，右侧原英雄技位让给合成）
            _craftBtn = MakeButton(_actionBar, "合  成", OnCraftClicked, new Color(0.55f, 0.4f, 0.7f), 22);
            _craftBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            _craftBtn.gameObject.SetActive(false);

            // 3代研习（2026-10-21）：有技能点时点亮，弹出可研习法术列表
            _learnBtn = MakeButton(_actionBar, "研  习", OnLearnClicked, new Color(0.25f, 0.6f, 0.6f), 22);
            _learnBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            _learnBtn.gameObject.SetActive(false);

            // 致命一击（2026-10-14）：对HP=1目标的限次斩杀，点亮后点击→选目标
            _deathblowBtn = MakeButton(_actionBar, "致命一击", OnDeathblowClicked, new Color(0.8f, 0.2f, 0.25f), 20);
            _deathblowBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            _deathblowBtn.gameObject.SetActive(false);

            // 骑士精神·追击（2026-10-14）：本回合封印过牌且未普攻 → 点击→确认弹窗（单目标）
            _chivalryBtn = MakeButton(_actionBar, "骑士追击", OnChivalryClicked, new Color(0.25f, 0.55f, 0.35f), 20);
            _chivalryBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            _chivalryBtn.gameObject.SetActive(false);

            _attackBtn = MakeButton(_actionBar, "攻  击", OnAttackClicked, new Color(0.85f, 0.3f, 0.25f), 24);
            _attackBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _endBtn = MakeButton(_actionBar, "结束回合", OnEndTurnClicked, new Color(0.3f, 0.55f, 0.85f), 24);
            _endBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _cancelBtn = MakeButton(_actionBar, "取消选目标", OnCancelTargetClicked, new Color(0.5f, 0.5f, 0.55f), 20);
            _cancelBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            _cancelBtn.gameObject.SetActive(false);

            // 开局调度阶段专用按钮（2026-10-20）：平时隐藏，调度轮到自己时出现
            _mulliganConfirmBtn = MakeButton(_actionBar, "确认调度", OnMulliganConfirm, new Color(0.85f, 0.55f, 0.15f), 22);
            _mulliganConfirmBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            _mulliganConfirmBtn.gameObject.SetActive(false);
            _mulliganSkipBtn = MakeButton(_actionBar, "跳过调度", OnMulliganSkip, new Color(0.5f, 0.5f, 0.55f), 20);
            _mulliganSkipBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            _mulliganSkipBtn.gameObject.SetActive(false);

            var hintGO = UIObj("Hint", _actionBar);
            hintGO.AddComponent<LayoutElement>().flexibleHeight = 1;
            _hintText = AddText(hintGO, "", 16, new Color(0.75f, 0.78f, 0.82f), TextAnchor.UpperLeft);
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hintText.verticalOverflow = VerticalWrapMode.Truncate;

            // 中央横幅（关键动作即时反馈，自动淡出，不拦点击）
            var bannerGO = UIObj("Banner", root);
            SetRect(bannerGO, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
                new Vector2(-600, -50), new Vector2(600, 50));
            _banner = AddText(bannerGO, "", 36, new Color(1f, 0.85f, 0.3f, 0f), TextAnchor.MiddleCenter);
            _banner.raycastTarget = false;

            BuildModal(root);
            BuildHelp(root);
        }

        void BuildModal(RectTransform root)
        {
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
            SetRect(bodyGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -150), new Vector2(-24, -72));
            _modalBody = AddText(bodyGO, "", 18, Color.white, TextAnchor.UpperLeft);

            // 两行内容区（勒索牌型多，一行放不下）
            var btns2GO = UIObj("Buttons2", panel.transform);
            SetRect(btns2GO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 158), new Vector2(-24, 238));
            var hlg2 = btns2GO.AddComponent<HorizontalLayoutGroup>();
            hlg2.spacing = 12;
            hlg2.childForceExpandWidth = false;
            hlg2.childForceExpandHeight = true;
            hlg2.childAlignment = TextAnchor.MiddleCenter;
            _modalBtns2 = (RectTransform)btns2GO.transform;

            var btnsGO = UIObj("Buttons", panel.transform);
            SetRect(btnsGO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 20), new Vector2(-24, 150));
            var hlg = btnsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            _modalBtns = (RectTransform)btnsGO.transform;

            _modal.SetActive(false);
        }

        // ==================================================================
        // 规则帮助浮层（M 键）：第1页胜利规则，第2页卡牌一览（可滚动）
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
            // 复盘模式专用：一键把整份日志（策略揭晓+全量历史）复制到系统剪贴板（2026-10-14）
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
            // 复盘看完把终局面板叫回来（终局面板打开时 HideModal 过）
            if (wasReview && _e != null && _e.GameOver && !_modal.activeSelf)
                ShowGameOverModal();
        }

        /// <summary>终局复盘：AI 策略揭晓（2026-10-13 用户裁定：对局中保密，复盘可查）+ 完整对局日志（引擎 History 全量），滚轮从上往下翻。</summary>
        void OpenReview()
        {
            _helpReview = true;
            _helpTitle.text = "对局复盘（共 " + _e.History.Count + " 条记录）";
            var sb = new System.Text.StringBuilder();
            sb.Append("【AI 本局策略】\n");
            foreach (var b in _e.Brains)
                if (b is AIBrain2 ai)
                    sb.Append(_e.Players[ai.Seat].Name).Append("：").Append(ai.StrategyName).Append("\n");
            // 终局信息：赢家剩余手牌构成（2026-10-20 用户要求）
            int wSeat = _e.WinnerSeat;
            if (wSeat >= 0)
            {
                var wh = _e.Players[wSeat].Hand;
                sb.Append("\n【赢家手牌】").Append(_e.Players[wSeat].Name)
                  .Append(" 终局剩余 ").Append(wh.Count).Append(" 张：");
                var groups = wh.GroupBy(c => c.Type).OrderByDescending(g => g.Count());
                sb.Append(wh.Count == 0 ? "（空手）" : string.Join(" ",
                    groups.Select(g => (g.Key == CardType.Hero ? _e.Players[wSeat].HeroName : g.Key.Name()) + "×" + g.Count())));
                sb.Append("\n");
            }
            sb.Append("\n────────\n\n");
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

        // 复盘模式：整份日志（策略揭晓+全量历史）一键进系统剪贴板（2026-10-14 用户要求：导出复盘）
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
                "将对手的英雄血量清零，成为最后存活的玩家即获胜。\n\n" +
                "【回合规则】\n" +
                "· 轮到你的回合法力（MP/蓝）回满，发动技能消耗法力。\n" +
                "· 基础攻击每回合1次：随机抽走对手1张手牌，置入弃牌堆。\n" +
                "· 每2回合从牌堆自动补充1张手牌（牌堆耗尽即止）。\n" +
                "· 开局每人" + (_g3 != null ? "10" : "9") + "张手牌＋1张永不离开的英雄牌" + (_g3 != null ? "（3代法术牌入库稀释补偿，比1/2代多1张）" : "") + "。\n\n" +
                "【英雄系统】\n" +
                "每局随机分配1名英雄，带专属被动/限定技，是重复游玩的核心。\n" +
                "点击英雄牌本体 = 发动英雄技 / 查看信息。\n\n" +
                "【合成牌】\n" +
                "消耗1行动点＋配方指定的3张牌 → 合成1张2点生命的合成牌，并获得【护盾】：持续2回合，吸收1点伤害（保护本体与整个手牌）。\n" +
                "点击【合成】按钮查看当前可合成的配方。\n\n" +
                "【快捷键】\n" +
                "空格 = 结束回合　　M = 规则帮助　　Esc = 退出游戏";
        }

        /// <summary>卡牌一览：直接读 CardDefs 元数据表和牌库构成，牌改动后这里自动跟随，不会漂移。</summary>
        string CardsPageText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【英雄牌】开局随机分配1张，永不离手");
            sb.AppendLine(CardDefs.Of(CardType.Hero).PassiveDesc);
            sb.AppendLine();
            sb.AppendLine("【牌库构成】");
            foreach (var kv in _boot.DeckSpec())
            {
                var def = CardDefs.Of(kv.Key);
                sb.AppendLine("【" + def.Name + "】×" + kv.Value);
                sb.AppendLine(def.DisplayDesc);
                sb.AppendLine();
            }
            sb.AppendLine("【合成牌】不在牌库中，由配方合成（1行动点＋3张配方牌，合成后获得2回合护盾）");
            foreach (CardType t in new[] { CardType.Priestess, CardType.Valkyrie, CardType.Paladin,
                                           CardType.Jailer, CardType.Inquisitor })
            {
                var def = CardDefs.Of(t);
                sb.AppendLine("【" + def.Name + "】");
                sb.AppendLine(def.DisplayDesc);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ==================================================================
        // 刷新（引擎状态 → 界面）：由Bootstrap循环与玩家动作驱动，不在Update里跑
        // ==================================================================

        public void Refresh()
        {
            if (_e == null) return;
            _legal = (_e.GameOver || _e.Pending != null)
                ? new List<GameAction>()
                : _e.GetLegalActions(_me);

            var cur = _e.Players[_e.Current];
            bool mulliganMine = !_e.GameOver && _e.Pending == null && _e.InMulligan && _e.Current == _me;
            _turnText.text = _e.GameOver
                ? "对 局 结 束"
                : _e.InMulligan
                    ? ("开 局 调 度 阶 段 · " + (mulliganMine
                        ? "轮到你了（调度券×" + _e.MulliganTickets[_me] + "）"
                        : cur.Name + " 调度中…"))
                    : ("第 " + _e.TurnNumber + " 轮 · " + (cur.IsHuman ? "你的回合" : cur.Name + " 的回合"));

            var meP = _e.Players[_me];
            string myFx = CountdownFx(meP);   // 2026-10-15：🍎青苹果/💣炸弹倒数图标，并入右上角状态栏
            string gen3Fx = "";
            if (_g3 != null)
            {
                // 3代：技能点 / 研习中 / 已被清晰术标记的牌数
                var learnT = _g3.LearningSpell(_me);
                int myMarked = _g3.MarkedCards(_me).Count;
                gen3Fx = "  技能点 " + meP.SkillPoints
                    + (learnT != 0 ? "  研习中【" + learnT.Name() + "】" : "")
                    + (myMarked > 0 ? "  被标记" + myMarked + "张" : "");
            }
            _infoText.text = "【" + meP.HeroName + "】 HP " + Mathf.Max(meP.HP, 0) + "/" + meP.MaxHP
                + "  MP " + meP.MP + "/" + meP.MaxMP
                + LevelText(meP)   // 2026-10-21：英雄等级与升级进度常驻显示（此前缺失，玩家看不到经验/升级）
                + "  弃牌堆 " + _e.Discard.Count + "  牌堆 " + _e.SideDeck.Count
                + (_e.IsSilenced(_me) ? "  【被沉默】" : "")
                + gen3Fx
                + (myFx.Length > 0 ? "  " + myFx : "");

            RebuildOpponents();
            RebuildHand();
            if (_e.InMulligan) ApplyMulliganLift();   // 重建会重置位置，选中牌重新上推

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me && meP.Alive;
            _craftBtn.gameObject.SetActive(myTurn && !_e.InMulligan && _legal.Any(a => a.Kind == ActionKind.Craft));
            if (_learnBtn != null)
                _learnBtn.gameObject.SetActive(myTurn && !_e.InMulligan && _legal.Any(a => a.Kind == ActionKind.Learn));
            _deathblowBtn.gameObject.SetActive(myTurn && !_e.InMulligan && _legal.Any(a => a.Kind == ActionKind.Deathblow));
            _chivalryBtn.gameObject.SetActive(myTurn && !_e.InMulligan && _legal.Any(a => a.Kind == ActionKind.ChivalryStrike));
            _mulliganConfirmBtn.gameObject.SetActive(mulliganMine);
            _mulliganSkipBtn.gameObject.SetActive(mulliganMine);
            if (mulliganMine)
            {
                // 确认按钮标签跟随已选张数；选中数受剩余调度券约束（ToggleMulliganPick 里已限）
                var label = _mulliganConfirmBtn.GetComponentInChildren<Text>();
                if (label != null)
                    label.text = _mulliganPicks.Count == 0 ? "确认调度" : "确认调度（已选 " + _mulliganPicks.Count + " 张）";
                _mulliganConfirmBtn.interactable = _mulliganPicks.Count > 0;
            }
            else if (_mulliganPicks.Count > 0)
            {
                _mulliganPicks.Clear();   // 阶段结束（确认/跳过/券用尽）清掉残留选中
            }
            // 调度阶段入场特效：横幅闪现一次（引擎日志横幅随后会被新日志接管，不冲突）
            if (mulliganMine && !_mulliganWasMine && _banner != null)
            {
                _banner.text = "调 度 阶 段\n点击手牌选中要替换的牌（会上推），点右侧【确认调度】换牌";
                _bannerAlpha = 1f;
                var bc = _banner.color; bc.a = 1f; _banner.color = bc;
            }
            _mulliganWasMine = mulliganMine;
            // 调度阶段手牌区背景染色（特效的一部分）：自己调度时橙色提示，对手调度时灰色
            var handImg = _handArea.GetComponent<Image>();
            if (handImg != null)
                handImg.color = _e.InMulligan
                    ? (mulliganMine ? new Color(0.30f, 0.22f, 0.12f) : new Color(0.14f, 0.14f, 0.18f))
                    : new Color(0.16f, 0.17f, 0.24f);
            _attackBtn.interactable = myTurn && !_e.InMulligan && _legal.Any(a => a.Kind == ActionKind.Attack);
            _endBtn.interactable = myTurn && !_e.InMulligan;
            _cancelBtn.gameObject.SetActive(_targeting != null && myTurn);

            UpdateHint(myTurn);
            RefreshLog();
            RebuildPendingButtons();

            if (_e.GameOver)
            {
                // 结算弹窗优先，帮助浮层让位；但复盘模式下保持打开（终局后本方法每帧被调用）
                if (!_helpReview) CloseHelp();
                if (!_gameOverShown) { _gameOverShown = true; ShowGameOverModal(); }
            }
            else if (_e.Pending != null && _e.Pending.Seat == _me && _e.Pending.Kind != ChoiceKind.DeathrattleAttack)
            {
                CloseHelp();   // 挡刀/贿赂等抉择弹窗优先，帮助浮层让位
                if (PendingSig(_e.Pending) != _modalSig) { _modalSig = PendingSig(_e.Pending); ShowPendingModal(_e.Pending); }
            }
            else
            {
                _modalSig = null;
                if (!_modalSticky) HideModal();
            }
        }

        static string PendingSig(PendingChoice pc)
        {
            if (pc == null) return null;
            return pc.Kind + ":" + pc.Seat + ":"
                + (pc.DrawnCard != null ? pc.DrawnCard.Id : 0) + ":"
                + (pc.Options != null ? pc.Options.Count : 0);
        }

        // 抉择主通道：操作栏按钮（已验证可靠的区域）
        void RebuildPendingButtons()
        {
            var pc = (_e.Pending != null && _e.Pending.Seat == _me) ? _e.Pending : null;
            string sig = PendingSig(pc);
            if (sig == _pendingSig) return;
            _pendingSig = sig;

            foreach (var go in _pendingBtns) Destroy(go);
            _pendingBtns.Clear();
            if (pc == null) return;

            if (pc.Kind == ChoiceKind.GuardIntercept)
            {
                // 可挡刀的牌逐张给按钮（侍卫/女武神/圣骑士；牺牲英雄还有牧师），玩家自己选牺牲哪张
                if (pc.Interceptors != null)
                    foreach (var it in pc.Interceptors)
                    {
                        var card = it;
                        AddPendingButton("用【" + card.Type.Name() + "】挡刀（保住【" + pc.DrawnCard.Type.Name() + "】）",
                            () => { _e.SubmitGuardChoice(true, card.Id); Refresh(); }, new Color(0.8f, 0.55f, 0.15f));
                    }
                AddPendingButton("不挡（【" + pc.DrawnCard.Type.Name() + "】承担结果）",
                    () => { _e.SubmitGuardChoice(false); Refresh(); }, new Color(0.5f, 0.5f, 0.55f));
            }
            else if (pc.Kind == ChoiceKind.PickFromDiscard)
            {
                foreach (var c in pc.Options.Take(4))
                {
                    var card = c;
                    AddPendingButton(pc.Reason + "：拿回【" + card.Type.Name() + "】",
                        () => { _e.SubmitDiscardPick(card.Id); Refresh(); }, new Color(0.3f, 0.55f, 0.85f));
                }
            }
            else if (pc.Kind == ChoiceKind.DeathrattleAttack)
            {
                foreach (var p in _e.Players)
                {
                    if (!p.Alive || p.Seat == _me || p.Hand.Count == 0 || _e.IsProtected(_me, p.Seat)) continue;
                    int seat = p.Seat;
                    AddPendingButton("亡语反击：攻击 " + p.Name,
                        () => { _e.SubmitDeathrattleTarget(seat); Refresh(); }, new Color(0.85f, 0.45f, 0.2f));
                }
            }
            else if (pc.Kind == ChoiceKind.CometCounter)
            {
                // 3代·彗星反制（2026-10-21）：按钮已移入抉择弹窗（弹窗是全屏遮罩，这里加了也会被盖住点不到）
            }
            else if (pc.Kind == ChoiceKind.ClarityPick)
            {
                // 3代·清晰术（2026-10-21 重做）：看库顶3张，选1张入手（逐张给按钮）
                foreach (var c in pc.Options)
                {
                    var card = c;
                    AddPendingButton(pc.Reason + "：拿【" + card.Type.Name() + "】入手",
                        () => { if (_g3 != null) _g3.SubmitClarityPick(card.Id); Refresh(); }, new Color(0.3f, 0.55f, 0.85f));
                }
            }
            else if (pc.Kind == ChoiceKind.IgnitePick)
            {
                // 3代·点燃（2026-10-21② 用户强化）：看目标对手2张手牌，选1张点燃（逐张给按钮）
                foreach (var c in pc.Options)
                {
                    var card = c;
                    AddPendingButton("点燃 " + pc.Reason + " 的【" + card.Type.Name() + "】",
                        () => { if (_g3 != null) _g3.SubmitIgnitePick(card.Id); Refresh(); }, new Color(0.9f, 0.4f, 0.2f));
                }
            }
        }

        void AddPendingButton(string label, UnityAction act, Color color)
        {
            var btn = MakeButton(_actionBar, label, act, color, 18);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            // 插在【攻击】按钮上方：操作栏前部新增了英雄技按钮，固定下标会错位
            btn.transform.SetSiblingIndex(_attackBtn.transform.GetSiblingIndex() + _pendingBtns.Count);
            _pendingBtns.Add(btn.gameObject);
        }

        void UpdateHint(bool myTurn)
        {
            if (_e.GameOver) { _hintText.text = ""; return; }
            if (_e.Pending != null) { _hintText.text = "请在上方操作栏或弹窗中作出抉择。"; return; }
            if (_e.InMulligan)
            {
                // 开局调度阶段（2026-10-20）：专属提示，避免被常规回合提示误导
                _hintText.text = myTurn && _e.Current == _me
                    ? "【开局调度】你有 " + _e.MulliganTickets[_me] + " 张调度券：\n"
                      + "· 点击手牌选中/取消（选中的牌会向上推起）\n"
                      + "· 点右侧【确认调度】换掉全部已选牌（1券=1张，换哪张不公开）\n"
                      + "· 不想换就点【跳过调度】"
                    : "开局调度阶段：" + _e.Players[_e.Current].Name + " 正在换牌……";
                return;
            }
            if (!myTurn) { _hintText.text = "对手行动中……"; return; }
            if (_targeting != null) { _hintText.text = "点击上方一名对手作为目标。"; return; }
            var spec = _e.HeroOf(_me);
            _hintText.text =
                "轮到你行动（MP " + _e.Players[_me].MP + " 点）：\n" +
                "· 点亮的牌可点击发动技能（耗蓝）\n" +
                "· 点击英雄牌本体可发动英雄技 / 查看信息\n" +
                (_g3 != null ? "· 3代：升级得技能点 → 点【研习】学会法术，法术牌学会后才可释放；有人施法时可用【彗星】反制\n" : "") +
                "· 【合成】按钮：1行动点＋配方3张牌→合成牌\n" +
                (_legal.Any(a => a.Kind == ActionKind.Deathblow) ? "· 【致命一击】按钮：点1血敌人完成斩杀（每目标限1次）\n" : "") +
                (_legal.Any(a => a.Kind == ActionKind.ChivalryStrike) ? "· 【骑士追击】按钮：攻击本回合被你警察封印的牌\n" : "") +
                "· " + spec.Desc + "\n" +
                (_e.BasicAttackUsed ? "（本回合基础攻击已用）\n" : "") +
                "· 行动完毕后【结束回合】或按 空格\n" +
                "· M 随时查看规则帮助";
        }

        void RefreshLog()
        {
            int start = Mathf.Max(0, _log.Count - LogShow);
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < _log.Count; i++) sb.AppendLine(_log[i]);
            _logText.text = sb.ToString();
        }

        void OnEngineLog(string msg)
        {
            _log.Add(msg);
            if (_log.Count > LogKeep) _log.RemoveRange(0, _log.Count - LogKeep);
            if (_banner != null)
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
            // 只负责横幅淡出与Esc，不做Refresh（刷新由Bootstrap循环与玩家动作驱动）
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
                // 空格 = 结束回合：与【结束回合】按钮同一门槛（自己回合、无弹窗、无待决、非调度阶段）
                if (Input.GetKeyDown(KeyCode.Space)
                    && _e != null && !_e.GameOver && _e.Pending == null
                    && _e.Current == _me && _e.Players[_me].Alive && !_e.InMulligan
                    && !_modal.activeSelf)
                    OnEndTurnClicked();
            }
        }

        void OnQuitClicked()
        {
            _modalSticky = true;
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

        // ==================================================================
        // 对手面板
        // ==================================================================

        void RebuildOpponents()
        {
            // 签名锁：对手状态没变就不重建。点击需要"按下与松开发送给同一对象"，
            // 频繁 Destroy 重建会让点击永远不成；这里保证静态期间面板对象稳定存在。
            var sig = OppSig();
            if (sig == _oppSig) return;
            _oppSig = sig;

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
                string info;
                if (p.Alive)
                {
                    info = "【" + p.HeroName + "】 HP " + p.HP + "/" + p.MaxHP +
                           "\n" + LevelText(p) +
                           "\n手牌: " + p.Hand.Count + " 张";
                    if (_e.BribedBy(_me) == p.Seat) info += "\n（已贿赂，不能攻击你）";
                    if (_e.HasArmGuard(p.Seat)) info += "\n（带刀侍卫）";
                    if (_e.IsSilenced(p.Seat)) info += "\n（沉默中）";
                    if (_e.GuardProtectedCount(p.Seat) > 0) info += "\n（" + _e.GuardProtectedCount(p.Seat) + "张被守卫护住）";
                    string fx = CountdownFx(p);   // 2026-10-15：对手的青苹果/炸弹倒数提示
                    if (fx.Length > 0) info += "\n" + fx;
                    if (_g3 != null)
                    {
                        // 3代：清晰术标记（明置）与点燃倒数（点燃时被公开）展示在对手面板上
                        foreach (var m in _g3.MarkedCards(p.Seat))
                            info += "\n🔍明置:【" + m.Type.Name() + "】";
                        foreach (var c in p.Hand)
                        {
                            int ig = _g3.IgniteRemaining(c);
                            if (ig >= 0) info += "\n🔥点燃:【" + c.Type.Name() + "】" + ig + "回合后阵亡";
                        }
                    }
                    if (isTarget) info += "\n▼ 点击选为目标 ▼";
                }
                else info = "☠";
                AddText(cardsGO, info, 17,
                    p.Alive ? new Color(0.8f, 0.85f, 0.9f) : new Color(0.6f, 0.4f, 0.4f), TextAnchor.MiddleCenter);

                if (isTarget)
                {
                    int seat = p.Seat;
                    var btn = panel.AddComponent<Button>();
                    btn.onClick.AddListener(() => OnTargetPicked(seat));
                }
            }
        }

        /// <summary>对手面板的重建签名：任何可见状态变化都会体现在串里。</summary>
        string OppSig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_e.GameOver ? 1 : 0).Append('/');
            foreach (var p in _e.Players)
            {
                if (p.Seat == _me) continue;
                sb.Append(p.Seat)
                  .Append(p.Alive ? 1 : 0).Append(p.Hand.Count).Append(p.HP)
                  .Append(p.HeroLevel).Append(p.EXP)   // 2026-10-21：等级/经验变化触发重建
                  .Append(_e.IsSilenced(p.Seat) ? 1 : 0).Append(_e.HasArmGuard(p.Seat) ? 1 : 0)
                  .Append(_e.GuardProtectedCount(p.Seat))
                  .Append(_e.BribedBy(_me) == p.Seat ? 1 : 0)
                  .Append(_targeting != null && _targeting.Any(a => a.TargetSeat == p.Seat) ? 1 : 0)
                  .Append(CountdownFx(p))   // 2026-10-15：青苹果/炸弹倒数变化也要触发重建
                  .Append(SpellFxSig(p.Seat))   // 2026-10-21：3代标记/点燃变化也要触发重建
                  .Append(';');
            }
            return sb.ToString();
        }

        /// <summary>倒数特效图标串（2026-10-15）：🍎N=青苹果倒数（N回合后自动吃掉回血），
        /// 💣N=其名下炸弹倒数（N个其回合后引爆），🔥N=被点燃的牌（N回合后该牌阵亡，3代）。
        /// 无倒数时返回空串。</summary>
        string CountdownFx(PlayerState p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in p.Hand.Where(c => c.Type == CardType.GreenApple && c.AppleTimer > 0))
                sb.Append("🍎").Append(c.AppleTimer).Append(' ');
            if (_e.HasTickingBomb(p.Seat))
            {
                var bomb = p.Hand.FirstOrDefault(c => c.BombActive && c.BombPlanter == p.Seat);
                int rem = bomb != null ? Mathf.Max(1, bomb.BombDueTurn - p.OwnTurnCount) : 0;
                sb.Append("💣").Append(rem > 0 ? rem.ToString() : "");
            }
            if (_g3 != null)
            {
                // 2026-10-22：点燃倒数（点燃是公开信息），🔥N 与 🍎💣 同款紧凑图标
                foreach (var c in p.Hand)
                {
                    int ig = _g3.IgniteRemaining(c);
                    if (ig >= 0) sb.Append("🔥").Append(ig).Append(' ');
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>英雄等级与升级进度（2026-10-21）：Lv(已升级次数+1)，未满级显示 EXP 进度（消灭数/下次升级门槛），
        /// 升满显示"满级"。消灭6张升2级、8张升3级（每局最多2次）。</summary>
        string LevelText(PlayerState p)
        {
            int lv = p.HeroLevel + 1;
            if (p.HeroLevel >= GameEngine2.MAX_HERO_LEVEL) return " Lv" + lv + " 满级";
            int need = p.HeroLevel == 0 ? GameEngine2.XP_LEVEL_2 : GameEngine2.XP_LEVEL_3;
            return " Lv" + lv + " EXP " + p.EXP + "/" + need;
        }

        /// <summary>3代法术特效签名（2026-10-21）：清晰术标记 + 点燃倒数。2代恒为空串。</summary>
        string SpellFxSig(int seat)
        {
            if (_g3 == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var m in _g3.MarkedCards(seat)) sb.Append('M').Append(m.Id).Append(',');
            foreach (var c in _e.Players[seat].Hand)
            {
                int ig = _g3.IgniteRemaining(c);
                if (ig >= 0) sb.Append('I').Append(c.Id).Append(ig).Append(',');
            }
            return sb.ToString();
        }

        // ==================================================================
        // 手牌区
        // ==================================================================

        static readonly CardType[] DisplayOrder =
        {
            CardType.Hero, CardType.Arsenal, CardType.GrandMinister, CardType.Witch, CardType.ArmedGuard,
            CardType.Exorcist, CardType.Police, CardType.Priest, CardType.Warden,
            CardType.Minister, CardType.Undead, CardType.Guard, CardType.Maid,
            CardType.GreenApple, CardType.Terrorist, CardType.Worker
        };

        void RebuildHand()
        {
            // 签名锁：手牌没变就不重建（理由同 OppSig——保点击稳定 + 省性能）
            var sig = HandSig();
            if (sig == _handSig) return;
            _handSig = sig;

            foreach (Transform child in _handArea) Destroy(child.gameObject);

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me;
            bool mulliganMine = myTurn && _e.InMulligan;   // 2026-10-20：调度阶段高亮全部非英雄手牌
            var sorted = _e.Players[_me].Hand
                .OrderBy(c => System.Array.IndexOf(DisplayOrder, c.Type))
                .ThenBy(c => c.Id);

            foreach (var c in sorted)
            {
                bool usable = (myTurn && _legal.Any(a => a.SourceCardId == c.Id))
                    || (_g3 != null && myTurn && GameEngine3.IsSpellType(c.Type)
                        && _legal.Any(a => a.Kind == ActionKind.Learn && a.TargetType == c.Type))   // 3代：未学会的法术牌可点（走研习）
                    || (mulliganMine && c.Type != CardType.Hero);   // 调度阶段：非英雄牌均可选（引擎口径一致）
                var card = c;
                MakeCardView(card, _handArea, mulliganMine ? (card.Type == CardType.Hero ? "" : "可调度") : SubLabel(card),
                    usable, myTurn || mulliganMine, 124,
                    () => OnCardClicked(card));
            }
        }

        /// <summary>调度阶段选中牌的"上推"表现：直接改 anchoredPosition（不触发手牌重建，选中态变化靠本方法刷新）。
        /// 顶部加"已选"小标。注意布局组重排（手牌重建）会重置位置，重建后需重调本方法。</summary>
        void ApplyMulliganLift()
        {
            foreach (Transform child in _handArea)
            {
                int id;
                var rt = child as RectTransform;
                if (rt == null || !child.name.StartsWith("Card_")
                    || !int.TryParse(child.name.Substring(5), out id)) continue;
                bool picked = _mulliganPicks.Contains(id);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, picked ? 26f : 0f);
                var mark = child.Find("SelMark");
                if (picked && mark == null)
                {
                    var markGO = UIObj("SelMark", child);
                    SetRect(markGO, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(-28, -30), new Vector2(28, -2));
                    AddText(markGO, "已选", 15, new Color(1f, 0.85f, 0.25f), TextAnchor.MiddleCenter);
                }
                else if (!picked && mark != null) Destroy(mark.gameObject);
            }
        }

        /// <summary>手牌区的重建签名：牌集合、每张牌的动态状态、可用高亮，变了才重建。</summary>
        string HandSig()
        {
            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me;
            var sb = new System.Text.StringBuilder();
            sb.Append(myTurn ? 1 : 0).Append(myTurn && _e.InMulligan ? 1 : 0).Append('/');
            foreach (var c in _e.Players[_me].Hand.OrderBy(c => c.Id))
            {
                sb.Append(c.Id)
                  .Append(_e.UsedThisTurn(c) ? 1 : 0).Append(c.PoisonUsed ? 1 : 0).Append(c.AntidoteUsed ? 1 : 0)
                  .Append(c.BombActive ? 1 : 0)
                  .Append(myTurn && _legal.Any(a => a.SourceCardId == c.Id) ? 1 : 0);
                if (_g3 != null)   // 3代：法术学习状态/被标记/点燃倒数变化都要触发重建
                    sb.Append(GameEngine3.IsSpellType(c.Type)
                        ? (_g3.HasSpell(_me, c.Type) ? 1 : 0).ToString() + (int)_g3.LearningSpell(_me)
                        : "")
                      .Append(_g3.IsMarked(c) ? 1 : 0)
                      .Append(_g3.IgniteRemaining(c));
                sb.Append(';');
            }
            return sb.ToString();
        }

        Button MakeCardView(Card c, Transform parent, string caption, bool highlighted, bool interactable, float width, UnityAction onClick)
        {
            var go = UIObj("Card_" + c.Id, parent);
            var def = CardVisual.Of(c.Type);
            AddImage(go, highlighted ? UIKit.Lighten(def.Bg, 1.25f) : def.Bg);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            // 符号图标：有图标的牌型（女巫/警察/亡灵）图标置顶，文字收窄到底部；其余维持原状
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
                19, def.Text, TextAnchor.MiddleCenter);

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.highlightedColor = Color.white;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>2代手牌副标签：查 CardDefs 表拼"X蓝·动作"，动态状态在这里覆盖。</summary>
        string SubLabel(Card c)
        {
            if (_e.IsGuardProtected(c)) return "护佑中";   // 2026-10-15 守卫改版：无法选取状态优先显示
            if (_g3 != null && GameEngine3.IsSpellType(c.Type))
            {
                // 3代法术牌（2026-10-21）：标记/点燃 > 学习状态 > 释放可用性
                if (_g3.IsMarked(c)) return "🔍被标记·可被攻击";
                int ig = _g3.IgniteRemaining(c);
                if (ig >= 0) return "🔥" + ig + "回合后阵亡";
                if (c.Type == CardType.Comet) return "被动·反制";
                if (_g3.LearningSpell(_me) == c.Type) return "研习中…";
                if (_e.IsSilenced(_me)) return "被沉默";
                if (!_g3.HasSpell(_me, c.Type)) return "未学会·白板";
                return CardDefs.Of(c.Type).SkillCost + "蓝·可释放";
            }
            if (c.Type == CardType.Witch)
                return (c.PoisonUsed && !_e.SkillsFree(_me) ? "毒✗" : "毒1蓝✓") + " " + (c.AntidoteUsed && !_e.SkillsFree(_me) ? "药✗" : "药1蓝✓");
            if (c.Type == CardType.GrandMinister && c.ExtortUsed && !_e.SkillsFree(_me)) return "限定已用";
            if (c.Type == CardType.Priestess)
            {
                if (_e.IsSilenced(_me)) return "被沉默";
                if (_e.CardSilenced(c)) return "被封印";
                bool free = _e.SkillsFree(_me);
                return (c.ReviveUsed && !free ? "复活✗" : "复活1蓝✓") + " " + (c.PrayerUsed && !free ? "祈祷✗" : "祈祷1蓝✓")
                     + (c.SynthHP >= 0 ? " 命" + c.SynthHP : "");
            }
            var def = CardDefs.Of(c.Type);
            if (c.Type == CardType.Terrorist)
            {
                if (c.BombActive) return "炸弹倒数中";
                if (_e.IsSilenced(_me)) return "被沉默";
                if (_e.CardSilenced(c)) return "被封印";
                return _e.UsedThisTurn(c) && !_e.SkillsFree(_me) ? "本回合已用" : def.SkillCost + "蓝·" + def.SkillTag;
            }
            if (c.Type == CardType.Worker) return "行动点+1";   // 2代口径（1代是生存牌，标签不同）
            if (def.HasActiveSkill)
            {
                if (_e.IsSilenced(_me)) return "被沉默";
                if (_e.CardSilenced(c)) return "被封印";
                return (_e.UsedThisTurn(c) && !_e.SkillsFree(_me)) ? "本回合已用" : def.SkillCost + "蓝·" + def.SkillTag;
            }
            return def.PassiveTag;
        }

        // ==================================================================
        // 玩家操作
        // ==================================================================

        void OnAttackClicked()
        {
            EnterTargeting(_legal.Where(a => a.Kind == ActionKind.Attack).ToList());
        }

        // 致命一击（2026-10-14）：可能有多个1血目标 → 复用选目标流程
        void OnDeathblowClicked()
        {
            EnterTargeting(_legal.Where(a => a.Kind == ActionKind.Deathblow).ToList());
        }

        // 骑士精神·追击（2026-10-14）：单目标（本回合被封印的那张牌），给确认弹窗防误触（耗蓝占普攻槽）
        void OnChivalryClicked()
        {
            var act = _legal.FirstOrDefault(a => a.Kind == ActionKind.ChivalryStrike);
            if (act == null) return;
            var t = _e.Players[act.TargetSeat];
            _modalSticky = true;
            ShowModalBase("骑士精神·追击",
                "挺枪直取 " + t.Name + " 被封印的【" + act.TargetType.Name() + "】！\n耗1行动点，并占用本回合的基础攻击。");
            AddModalTextButton("发动", () =>
            {
                _modalSticky = false; HideModal();
                _e.ApplyAction(act);
                StartCoroutine(BackswingThenRefresh(_boot.AttackBackswing));
            });
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        // ==================================================================
        // 3代·法术迸发（2026-10-21）：研习 / 施放法术
        // ==================================================================

        void OnLearnClicked()
        {
            var learns = _legal.Where(a => a.Kind == ActionKind.Learn).ToList();
            if (learns.Count == 0) return;
            _modalSticky = true;
            ShowModalBase("研  习",
                "消耗 1 技能点 + 1 行动点，研习一种法术：\n研习期间这张法术牌仍是纯白板，下个自己的回合开始才学会释放。\n（技能点：升级奖励，6消灭升2级得1点、8消灭升3级再得1点并回1点HP）");
            foreach (var a in learns)
            {
                var act = a;
                var def = CardDefs.Of(act.TargetType);
                var vis = CardVisual.Of(act.TargetType);   // 2026-10-22：研习按钮底色=法术牌卡背色
                AddModalTextButton("研习【" + def.Name + "】\n" + def.SkillDesc,
                    () => { _modalSticky = false; HideModal(); _e.ApplyAction(act); Refresh(); }, vis.Bg, vis.Text);
            }
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void OnSpellClicked(Card c)
        {
            var acts = _legal.Where(a => a.Kind == ActionKind.CastSpell && a.SourceCardId == c.Id).ToList();
            var def = CardDefs.Of(c.Type);
            switch (c.Type)
            {
                case CardType.Alchemy:
                    if (acts.Count == 0) { SpellInfoModal(c, "现在无法释放：需要先研习其它法术（炼金术=当场化作你已学会的任意法术），且蓝够、未被沉默。"); return; }
                    _modalSticky = true;
                    ShowModalBase("炼金术（1蓝）", def.SkillDesc);
                    foreach (var t in GameEngine3.SpellTypes.Where(x => x != CardType.Alchemy && x != CardType.Comet
                                                                     && acts.Any(x2 => x2.TargetType == x)))
                    {
                        var tt = t;
                        var same = acts.Where(x2 => x2.TargetType == tt).ToList();
                        if (same.Count == 1)
                            AddModalTextButton("化作【" + tt.Name() + "】释放",
                                () => { _modalSticky = false; HideModal(); _e.ApplyAction(same[0]); Refresh(); });
                        else
                            AddModalTextButton("化作【" + tt.Name() + "】释放…",
                                () => { _modalSticky = false; HideModal(); EnterTargeting(same); });
                    }
                    AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
                    return;
                case CardType.Barrier:
                    if (acts.Count == 0) { SpellInfoModal(c, "现在无法释放：蓝不够或被沉默。"); return; }
                    _modalSticky = true;
                    ShowModalBase("屏障（1蓝）", def.SkillDesc);
                    AddModalTextButton("释放", () => { _modalSticky = false; HideModal(); _e.ApplyAction(acts[0]); Refresh(); });
                    AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
                    return;
                case CardType.Thunder:
                    if (acts.Count == 0) { SpellInfoModal(c, "现在无法释放：蓝不够、被沉默，或没有有手牌的对手。"); return; }
                    EnterTargeting(acts);   // 按对手选目标，次数=其手牌数×25%四舍五入
                    return;
                case CardType.Ignite:
                    SpellPickSeatModal(c, "点燃（1蓝）：选择一名对手，随机缠上其手牌中的 1 张牌——2 回合后该牌阵亡（其手牌中的军火库同步炸毁）。", 1);
                    return;
                case CardType.Clarity:
                    if (acts.Count == 0) { SpellInfoModal(c, "现在无法释放：蓝不够、被沉默，或牌库已空。"); return; }
                    _modalSticky = true;
                    ShowModalBase("清晰术（1蓝）", def.SkillDesc);
                    AddModalTextButton("释放", () => { _modalSticky = false; HideModal(); _e.ApplyAction(acts[0]); Refresh(); });
                    AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
                    return;
                default:   // 彗星：纯被动反制，不可主动释放
                    SpellInfoModal(c, null);
                    return;
            }
        }

        /// <summary>点燃/清晰术选对手：手牌暗置，UI 选座位、牌由系统随机抽。</summary>
        void SpellPickSeatModal(Card spell, string desc, int seatsNeeded)
        {
            if (!_legal.Any(a => a.Kind == ActionKind.CastSpell && a.SourceCardId == spell.Id))
            {
                SpellInfoModal(spell, "现在无法释放：蓝不够、被沉默，或没有合法目标。");
                return;
            }
            _modalSticky = true;
            var chosen = new List<int>();
            System.Action rebuild = null;
            rebuild = () =>
            {
                ShowModalBase(spell.Type.Name(),
                    desc + (chosen.Count > 0 ? "\n\n已选：" + string.Join("、", chosen.Select(s => _e.Players[s].Name).ToArray()) : ""));
                foreach (var p in _e.Players)
                {
                    if (p.Seat == _me || !p.Alive || chosen.Contains(p.Seat)) continue;
                    if (!p.Hand.Any(x => x.Type != CardType.Hero)) continue;
                    int seat = p.Seat;
                    AddModalTextButton("选 " + p.Name + "（手牌 " + p.Hand.Count + " 张）", () =>
                    {
                        chosen.Add(seat);
                        if (chosen.Count < seatsNeeded) rebuild();
                        else { _modalSticky = false; HideModal(); CastSpellHidden(spell, chosen); }
                    });
                }
                if (chosen.Count > 0)
                    AddModalTextButton("重选", () => { chosen.Clear(); rebuild(); });
                AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
            };
            rebuild();
        }

        /// <summary>组装"暗置随机"施法行动：从每个被选座位随机取 1 张非英雄牌作目标。</summary>
        void CastSpellHidden(Card spell, List<int> seats)
        {
            var a = new GameAction { Kind = ActionKind.CastSpell, SourceCardId = spell.Id, TargetType = spell.Type };
            if (spell.Type == CardType.Ignite)
            {
                var t = _e.Players[seats[0]];
                var cand = t.Hand.Where(x => x.Type != CardType.Hero).ToList();
                if (cand.Count == 0) return;
                a.TargetSeat = seats[0];
                a.SecondCardId = cand[UnityEngine.Random.Range(0, cand.Count)].Id;
            }
            else if (spell.Type == CardType.Clarity)
            {
                var c1 = _e.Players[seats[0]].Hand.Where(x => x.Type != CardType.Hero).ToList();
                var c2 = _e.Players[seats[1]].Hand.Where(x => x.Type != CardType.Hero).ToList();
                if (c1.Count == 0 || c2.Count == 0) return;
                a.SecondCardId = c1[UnityEngine.Random.Range(0, c1.Count)].Id;
                a.ThirdCardId = c2[UnityEngine.Random.Range(0, c2.Count)].Id;
            }
            _e.ApplyAction(a);
            Refresh();
        }

        void SpellInfoModal(Card c, string extra)
        {
            _modalSticky = true;
            var def = CardDefs.Of(c.Type);
            string known = "";
            if (_g3 != null)
                known = _g3.HasSpell(_me, c.Type)
                    ? (c.Type == CardType.Comet ? "（已学会：可反制）" : "（已学会）")
                    : (c.Type == CardType.Comet ? "（未学会：不可反制，点【研习】点亮）" : "（未学会：纯白板，点【研习】点亮释放权限）");
            string body = def.SkillDesc + (string.IsNullOrEmpty(extra) ? "" : "\n\n" + extra);
            ShowModalBase(def.Name + known, body);
            AddModalTextButton("知道了", () => { _modalSticky = false; Refresh(); });
        }

        void OnCraftClicked()
        {
            // 合成（2026-10-06）：列出当前可合成的牌。固定配方点选即合成；
            // 含"任意"位的配方（女武神/圣骑士）需再手动点选第三张牌（2026-10-07）。
            var crafts = _legal.Where(a => a.Kind == ActionKind.Craft).ToList();
            if (crafts.Count == 0) return;
            _modalSticky = true;
            ShowModalBase("合  成", "消耗1行动点＋配方中的3张牌，合成一张2点生命的合成牌，并获得1点护盾（持续2回合，吸收1点伤害）：");
            foreach (var grp in crafts.GroupBy(a => a.TargetType))
            {
                var synth = grp.Key;
                var def = CardDefs.Of(synth);
                string desc = string.IsNullOrEmpty(def.SkillDesc) ? def.PassiveDesc : def.SkillDesc;
                var acts = grp.ToList();
                if (acts.All(a => a.SourceCardId <= 0))   // 固定配方：直接合成
                {
                    var act = acts[0];
                    var cost = _e.CraftCost(_me, synth);
                    string recipe = cost == null ? "" : string.Join("＋", cost.Select(c => c.Type.Name()));
                    AddModalTextButton("【" + synth.Name() + "】\n" + recipe + "\n" + desc,
                        () => { _modalSticky = false; HideModal(); _e.ApplyAction(act); Refresh(); });
                }
                else   // "任意"位：配方照常显示，点选目标后再手动点选第三张牌
                {
                    CardType?[] rec;
                    string recipe = GameEngine2.CraftRecipes.TryGetValue(synth, out rec)
                        ? string.Join("＋", rec.Select(rt => rt.HasValue ? rt.Value.Name() : "任意")) : "";
                    AddModalTextButton("【" + synth.Name() + "】\n" + recipe + "\n（点选后需再选择“任意”位的牌）\n" + desc,
                        () => ShowCraftWildcardModal(synth, acts));
                }
            }
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowCraftWildcardModal(CardType synth, List<GameAction> acts)
        {
            // "任意"配方第二级：逐张列出可作第三张的候选牌，点哪张消耗哪张
            _modalSticky = true;
            ShowModalBase("合成【" + synth.Name() + "】", "点选作为配方“任意”位的牌（这张牌将被消耗）：");
            foreach (var a in acts)
            {
                var act = a;
                var c = _e.Players[_me].Hand.FirstOrDefault(x => x.Id == a.SourceCardId);
                if (c == null) continue;
                AddModalTextButton("【" + c.Type.Name() + "】",
                    () => { _modalSticky = false; HideModal(); _e.ApplyAction(act); Refresh(); });
            }
            AddModalTextButton("返回", () => OnCraftClicked());
        }

        void OnEndTurnClicked()
        {
            _targeting = null;
            _e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
            Refresh();
        }

        void OnCancelTargetClicked()
        {
            _targeting = null;
            Refresh();
        }

        void EnterTargeting(List<GameAction> acts)
        {
            if (acts.Count == 0) return;
            _targeting = acts;
            Refresh();
        }

        void OnTargetPicked(int seat)
        {
            if (_targeting == null) return;
            var acts = _targeting.Where(a => a.TargetSeat == seat).ToList();
            if (acts.Count == 0) return;
            if (acts[0].Kind == ActionKind.Bribe) { ShowBribeGiveModal(acts); return; }
            _targeting = null;
            var kind = acts[0].Kind;
            _e.ApplyAction(acts[0]);
            // 攻击后摇：攻击结算后固定停顿再刷新——对手AI其实瞬间完成挡刀决策，
            // 统一停顿让"有没有侍卫"无法从响应快慢读出来
            if (kind == ActionKind.Attack || kind == ActionKind.ArsenalAttack || kind == ActionKind.PoisonAttack
                || kind == ActionKind.Deathblow || kind == ActionKind.ChivalryStrike)
                StartCoroutine(BackswingThenRefresh(_boot.AttackBackswing));
            else
                Refresh();
        }

        System.Collections.IEnumerator BackswingThenRefresh(float sec)
        {
            yield return new WaitForSeconds(sec);
            Refresh();
        }

        // ---- 开局调度阶段（2026-10-20）：选牌上推 + 右侧确认 ----

        void ToggleMulliganPick(Card c)
        {
            if (c.Type == CardType.Hero) return;   // 英雄牌不可调度（与引擎 GetLegalActions 口径一致）
            if (_mulliganPicks.Contains(c.Id)) _mulliganPicks.Remove(c.Id);
            else
            {
                if (_mulliganPicks.Count >= _e.MulliganTickets[_me]) return;   // 券不够就不再增选
                _mulliganPicks.Add(c.Id);
            }
            ApplyMulliganLift();
            Refresh();   // 刷新"确认调度（已选N张）"标签
        }

        void OnMulliganConfirm()
        {
            // 逐张换：每换一张引擎重算（补牌来自牌堆顶），券用尽或阶段结束自动停
            foreach (int id in _mulliganPicks.ToList())
            {
                if (!_e.InMulligan || _e.Current != _me) break;
                var legal = _e.GetLegalActions(_me);   // 每轮重查，不用缓存的 _legal
                var act = legal.FirstOrDefault(a => a.Kind == ActionKind.Mulligan && a.SourceCardId == id);
                if (act == null) break;   // 该牌已不在手/券已用完
                _e.ApplyAction(act);
            }
            _mulliganPicks.Clear();
            Refresh();
        }

        void OnMulliganSkip()
        {
            var act = _e.GetLegalActions(_me).FirstOrDefault(a => a.Kind == ActionKind.EndMulligan);
            if (act == null) return;
            _mulliganPicks.Clear();
            _e.ApplyAction(act);
            Refresh();
        }

        void OnCardClicked(Card c)
        {
            if (_e.GameOver || _e.Pending != null || _e.Current != _me) return;
            if (_e.InMulligan) { ToggleMulliganPick(c); return; }   // 2026-10-20：调度阶段点牌=选中/取消
            if (c.Type == CardType.Hero)
            {
                // 英雄技（2026-10-06 起）：点击英雄牌本体触发；没有可发动的英雄技时点开信息。
                // 2026-10-14：致命一击/骑士精神不是 HeroPower 行动，也从这里给入口。
                var power = _legal.FirstOrDefault(a => a.Kind == ActionKind.HeroPower);
                var chiv = _legal.FirstOrDefault(a => a.Kind == ActionKind.ChivalryStrike);
                var dbActs = _legal.Where(a => a.Kind == ActionKind.Deathblow).ToList();
                if (power == null && chiv == null && dbActs.Count == 0) { ShowHeroInfo(); return; }
                _modalSticky = true;
                var spec = _e.HeroOf(_me);
                ShowModalBase("英雄技", spec.Desc);
                if (power != null)
                    AddModalTextButton("发动英雄技", () => { _modalSticky = false; HideModal(); _e.ApplyAction(power); Refresh(); });
                if (chiv != null)
                    AddModalTextButton("骑士精神·追击", () => { _modalSticky = false; HideModal(); OnChivalryClicked(); });
                if (dbActs.Count > 0)
                    AddModalTextButton("致命一击（选目标）", () => { _modalSticky = false; HideModal(); EnterTargeting(dbActs); });
                AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
                return;
            }

            if (_g3 != null && GameEngine3.IsSpellType(c.Type)) { OnSpellClicked(c); return; }   // 3代法术牌
            var acts = _legal.Where(a => a.SourceCardId == c.Id).ToList();
            if (acts.Count == 0)
            {
                // 现在用不了 / 被动牌说明：文案来自 CardDefs 表
                _modalSticky = true;
                var def = CardDefs.Of(c.Type);
                string reason;
                if (_e.IsSilenced(_me))
                    reason = "你被【police station】沉默了：本回合技能全封（含被动），普攻仍可用。";
                else if (_e.UsedThisTurn(c))
                    reason = "本回合已用过。";
                else
                    reason = "蓝不够或没有合法目标。";
                string desc = def.HasActiveSkill
                    ? def.SkillDesc + "\n（现在用不了：" + reason + "）"
                    : def.DisplayDesc;
                if (c.SynthHP >= 0) desc = "（合成牌 · 剩余 " + c.SynthHP + " 点生命）\n" + desc;
                ShowModalBase(def.Name, desc);
                AddModalTextButton("知道了", () => { _modalSticky = false; Refresh(); });
                return;
            }

            switch (c.Type)
            {
                case CardType.Exorcist:
                case CardType.Police:
                case CardType.Arsenal:
                case CardType.Minister:
                case CardType.Jailer:
                case CardType.Inquisitor:
                    EnterTargeting(acts);
                    break;
                case CardType.Witch:
                    ShowWitchModal(acts);
                    break;
                case CardType.Priestess:
                    ShowPriestessModal(acts);
                    break;
                case CardType.GrandMinister:
                    ShowExtortModal(acts);
                    break;
                case CardType.Warden:
                    ShowHideModal(acts);
                    break;
                case CardType.ArmedGuard:
                case CardType.Terrorist:   // 安放炸弹：无目标，直接发动
                    _e.ApplyAction(acts[0]);
                    Refresh();
                    break;
            }
        }

        void ShowHeroInfo()
        {
            _modalSticky = true;
            var spec = _e.HeroOf(_me);
            ShowModalBase("英雄【" + _e.Players[_me].HeroName + "】",
                spec.Desc + "\nHP " + _e.Players[_me].HP + "/" + _e.Players[_me].MaxHP +
                "   MP " + _e.Players[_me].MP + "/" + _e.Players[_me].MaxMP +
                "\n" + CardDefs.Of(CardType.Hero).PassiveDesc);
            AddModalTextButton("知道了", () => { _modalSticky = false; Refresh(); });
        }

        // ==================================================================
        // 技能弹窗（辅助展示；抉择主通道在操作栏）
        // ==================================================================

        void ShowWitchModal(List<GameAction> acts)
        {
            _modalSticky = true;
            ShowModalBase("女巫", "选择一个技能（蓝耗照付）：");
            var poi = acts.Where(a => a.Kind == ActionKind.PoisonAttack).ToList();
            if (poi.Count > 0)
                AddModalTextButton("毒药（1蓝）：追加一次攻击", () => { _modalSticky = false; HideModal(); EnterTargeting(poi); });
            var ant = acts.FirstOrDefault(a => a.Kind == ActionKind.Antidote);
            if (ant != null)
                AddModalTextButton("解药（1蓝）：救活上一回合阵亡的角色牌", () => { _modalSticky = false; HideModal(); _e.ApplyAction(ant); Refresh(); });
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowPriestessModal(List<GameAction> acts)
        {
            _modalSticky = true;
            var c = _e.Players[_me].Hand.First(x => x.Id == acts[0].SourceCardId);
            ShowModalBase("女祭司（剩余 " + c.SynthHP + " 点生命）", "选择一个限定技（各限1次，蓝耗照付）：");
            var srv = acts.FirstOrDefault(a => a.Kind == ActionKind.SynthRevive);
            if (srv != null)
                AddModalTextButton("复活（1蓝）：从弃牌堆复活1张角色牌", () => { _modalSticky = false; HideModal(); _e.ApplyAction(srv); Refresh(); });
            var sp = acts.FirstOrDefault(a => a.Kind == ActionKind.SynthPrayer);
            if (sp != null)
                AddModalTextButton("祈祷（1蓝）：2回合后自身1点生命转化为英雄的1点生命", () => { _modalSticky = false; HideModal(); _e.ApplyAction(sp); Refresh(); });
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowExtortModal(List<GameAction> acts)
        {
            _modalSticky = true;
            ShowModalBase("军机大臣 · 勒索（1蓝·限定技）", "选择牌型：获得其他所有玩家手牌中的这种牌。选【英雄】= 所有对手各扣1点HP。整局限1次，save回合除外。");
            var list = acts.ToList();
            int half = (list.Count + 1) / 2;
            foreach (var a in list.Take(half)) AddTypeChip(_modalBtns2, a);
            foreach (var a in list.Skip(half)) AddTypeChip(_modalBtns, a);
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void AddTypeChip(Transform row, GameAction act)
        {
            var def = CardVisual.Of(act.TargetType);
            var go = UIObj("Type_" + act.TargetType, row);
            AddImage(go, def.Bg);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 118;
            var btn = go.AddComponent<Button>();
            var a = act;
            btn.onClick.AddListener(() => { _modalSticky = false; HideModal(); _e.ApplyAction(a); Refresh(); });
            var icon = CardVisual.IconOf(act.TargetType);
            if (icon != null)
            {
                var iconGO = UIObj("Icon", go.transform);
                SetRect(iconGO, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(-24, -56), new Vector2(24, -6));
                var img = iconGO.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
            }
            var txtGO = UIObj("Label", go.transform);
            if (icon != null)
                SetRect(txtGO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 2), new Vector2(-2, 52));
            else
                SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, act.TargetType.Name(), 18, def.Text, TextAnchor.MiddleCenter);
        }

        void ShowHideModal(List<GameAction> acts)
        {
            _modalSticky = true;
            ShowModalBase("守卫 · 护牌（1蓝）",
                "给一张手牌施加【无法选取】直到你的下回合开始：\n牌留在手牌里，但不会被攻击抽中/挡刀/驱魔/审判/扣押/勒索/贿赂/合成消耗。\n不能选英雄牌。（护住哪张牌不公开，对手只看到张数）");
            var shown = new HashSet<int>();
            foreach (var a in acts)
            {
                if (!shown.Add(a.SecondCardId)) continue;
                var act = a;
                var hide = _e.Players[_me].Hand.First(c => c.Id == act.SecondCardId);
                AddModalCard(hide, "护住", () => { _modalSticky = false; HideModal(); _e.ApplyAction(act); Refresh(); });
            }
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowBribeGiveModal(List<GameAction> bribeActs)
        {
            _modalSticky = true;
            int seat = bribeActs[0].TargetSeat;
            ShowModalBase("贿赂 " + _e.Players[seat].Name + "（1蓝）",
                "把一张牌交给对方：到你的下回合开始前，对方不能攻击你（含追攻和毒药）。");
            foreach (var a in bribeActs)
            {
                var act = a;
                var give = _e.Players[_me].Hand.First(c => c.Id == act.SecondCardId);
                AddModalCard(give, "交出", () =>
                { _modalSticky = false; HideModal(); _targeting = null; _e.ApplyAction(act); Refresh(); });
            }
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowPendingModal(PendingChoice pc)
        {
            if (pc.Kind == ChoiceKind.GuardIntercept)
            {
                ShowModalBase("遭遇攻击！", _e.Players[pc.AttackerSeat].Name + " 攻击了你，随机抽中了下面这张牌。\n点被抽中的牌 = 承担结果；点挡刀牌 = 挺身而出。");
                AddModalCard(pc.DrawnCard, "被抽中了", () => { _e.SubmitGuardChoice(false); Refresh(); });
                if (pc.Interceptors != null)
                    foreach (var it in pc.Interceptors)
                    {
                        var card = it;
                        AddModalCard(card, "挡刀", () => { _e.SubmitGuardChoice(true, card.Id); Refresh(); });
                    }
            }
            else if (pc.Kind == ChoiceKind.PickFromDiscard)
            {
                ShowModalBase(pc.Reason, "从弃牌堆选择一张角色牌回到手中：");
                foreach (var o in pc.Options)
                {
                    var opt = o;
                    AddModalCard(opt, "回收", () => { _e.SubmitDiscardPick(opt.Id); Refresh(); });
                }
            }
            else if (pc.Kind == ChoiceKind.CometCounter)
            {
                string caster = pc.AttackerSeat >= 0 ? _e.Players[pc.AttackerSeat].Name : "对手";
                ShowModalBase("彗星反制",
                    caster + " 释放了【" + pc.Reason + "】。\n你可以消耗 1 张手牌中的【彗星】抵消这个法术（被抵消的法术直接消耗、效果不结算）。");
                // 2026-10-21 修复：抉择按钮必须放弹窗内——弹窗是全屏遮罩，右侧操作栏的按钮被盖住点不到（曾致反制卡死）
                AddModalTextButton("消耗【彗星】抵消", () => { if (_g3 != null) _g3.SubmitCometChoice(true); Refresh(); });
                AddModalTextButton("不抵消（让法术结算）", () => { if (_g3 != null) _g3.SubmitCometChoice(false); Refresh(); });
            }
            else if (pc.Kind == ChoiceKind.ClarityPick)
            {
                ShowModalBase("清晰术 · 牌库顶的 " + pc.Options.Count + " 张牌",
                    "选择 1 张加入你的手牌（其余按原顺序放回牌库顶）。\n点下方卡牌或右侧操作栏按钮：");
                foreach (var o in pc.Options)
                {
                    var opt = o;
                    AddModalCard(opt, "拿走", () => { if (_g3 != null) _g3.SubmitClarityPick(opt.Id); Refresh(); });
                }
            }
            else if (pc.Kind == ChoiceKind.IgnitePick)
            {
                ShowModalBase("点燃 · 查看 " + pc.Reason + " 的 " + pc.Options.Count + " 张手牌",
                    "选择 1 张点燃：2 回合后这张牌将阵亡（其手牌中的军火库会同步炸毁）。");
                foreach (var o in pc.Options)
                {
                    var opt = o;
                    AddModalCard(opt, "点燃", () => { if (_g3 != null) _g3.SubmitIgnitePick(opt.Id); Refresh(); });
                }
            }
        }

        void ShowGameOverModal()
        {
            _modalSticky = true;
            bool win = _e.WinnerSeat == _me;
            ShowModalBase(win ? "你赢了！" : "对局结束",
                (win ? "你是最后的存活者！" : "胜者：" + _e.Players[_e.WinnerSeat].Name) +
                "\n共进行了 " + _e.TurnNumber + " 轮。");
            AddModalTextButton("复盘", () => { _modalSticky = false; HideModal(); OpenReview(); });
            AddModalTextButton("再来一局", () => { _modalSticky = false; _boot.Restart(); });
            AddModalTextButton("返回主菜单", () => { _modalSticky = false; _boot.BackToMenu(); });
        }

        // ==================================================================
        // 弹窗与零件（与初代同一套）
        // ==================================================================

        void ShowModalBase(string title, string body)
        {
            _modal.SetActive(true);
            _modalTitle.text = title;
            _modalBody.text = body;
            foreach (Transform child in _modalBtns) Destroy(child.gameObject);
            foreach (Transform child in _modalBtns2) Destroy(child.gameObject);
        }

        void HideModal()
        {
            _modal.SetActive(false);
            foreach (Transform child in _modalBtns) Destroy(child.gameObject);
            foreach (Transform child in _modalBtns2) Destroy(child.gameObject);
        }

        void AddModalCard(Card c, string caption, UnityAction onClick)
        {
            MakeCardView(c, _modalBtns, caption, true, true, 108, onClick);
        }

        void AddModalTextButton(string label, UnityAction act)
        {
            var btn = MakeButton(_modalBtns, label, act, new Color(0.3f, 0.45f, 0.7f), 20);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
        }

        /// <summary>带配色版本（2026-10-22）：研习界面法术按钮底色=该法术牌的卡背色，文字色同步用牌面文字色。</summary>
        void AddModalTextButton(string label, UnityAction act, Color bg, Color text)
        {
            var btn = MakeButton(_modalBtns, label, act, bg, 20);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
            var labelTxt = btn.GetComponentInChildren<Text>();
            if (labelTxt != null) labelTxt.color = text;
        }
    }
}
