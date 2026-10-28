using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2代对局界面。布局骨架与初代 GameUI 完全同款（已在你机器上验证过）：
    /// HLG childForceExpandHeight=true + 子元素只给 LayoutElement.preferredWidth、
    /// 操作栏按钮只给 preferredHeight、Update 不做每帧 Refresh（由 Bootstrap 循环和玩家动作驱动）、
    /// 抉择以操作栏按钮为主通道、弹窗为辅助展示。
    /// 2代新增：HP/MP与英雄显示、英雄技按钮、守卫藏牌/军机大臣勒索/女巫弹窗、亡语反击按钮。
    /// </summary>
    public class GameUI2 : MonoBehaviour
    {
        GameEngine2 _e;
        GameBootstrap2 _boot;
        Font _font;
        const int _me = 0;

        Text _turnText, _infoText, _logText, _hintText, _banner;
        float _bannerAlpha;
        RectTransform _oppArea, _handArea;
        Transform _actionBar;
        Button _powerBtn, _attackBtn, _endBtn, _cancelBtn;

        GameObject _modal;
        Text _modalTitle, _modalBody;
        RectTransform _modalBtns, _modalBtns2;
        string _modalSig, _pendingSig;
        bool _modalSticky, _gameOverShown;

        readonly List<string> _log = new List<string>();
        List<GameAction> _legal = new List<GameAction>();
        List<GameAction> _targeting;   // 非null = 正在选目标（攻击/技能/贿赂选对象）
        readonly List<GameObject> _pendingBtns = new List<GameObject>();

        const int LogKeep = 300, LogShow = 14;

        // ==================================================================
        // 初始化与搭建（布局与初代完全一致）
        // ==================================================================

        public void Init(GameEngine2 e, GameBootstrap2 boot)
        {
            _e = e;
            _boot = boot;
            _font = GameUI.LoadFont();
            _log.AddRange(_e.History);
            _e.OnLog += OnEngineLog;
            GameUI.EnsureEventSystem();
            BuildCanvas();
            Refresh();
        }

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

            // 手牌区（底部）
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

            // 操作栏（右下）
            var barGO = UIObj("ActionBar", root);
            SetRect(barGO, new Vector2(0.82f, 0), new Vector2(1, 0), new Vector2(8, 16), new Vector2(-16, 236));
            var barVlg = barGO.AddComponent<VerticalLayoutGroup>();
            barVlg.spacing = 10;
            barVlg.childForceExpandWidth = true;
            barVlg.childForceExpandHeight = false;
            barVlg.childAlignment = TextAnchor.UpperCenter;
            _actionBar = barGO.transform;

            _powerBtn = MakeButton(_actionBar, "英雄技", OnPowerClicked, new Color(0.75f, 0.6f, 0.15f), 22);
            _powerBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;

            _attackBtn = MakeButton(_actionBar, "攻  击", OnAttackClicked, new Color(0.85f, 0.3f, 0.25f), 24);
            _attackBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _endBtn = MakeButton(_actionBar, "结束回合", OnEndTurnClicked, new Color(0.3f, 0.55f, 0.85f), 24);
            _endBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            _cancelBtn = MakeButton(_actionBar, "取消选目标", OnCancelTargetClicked, new Color(0.5f, 0.5f, 0.55f), 20);
            _cancelBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            _cancelBtn.gameObject.SetActive(false);

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
        // 刷新（引擎状态 → 界面）：由Bootstrap循环与玩家动作驱动，不在Update里跑
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

            var meP = _e.Players[_me];
            _infoText.text = "【" + meP.HeroName + "】 HP " + Mathf.Max(meP.HP, 0) + "/" + meP.MaxHP
                + "  MP " + meP.MP + "/" + meP.MaxMP
                + "  弃牌堆 " + _e.Discard.Count + "  牌堆 " + _e.SideDeck.Count
                + (_e.IsSilenced(_me) ? "  【被沉默】" : "");

            RebuildOpponents();
            RebuildHand();

            bool myTurn = !_e.GameOver && _e.Pending == null && _e.Current == _me && meP.Alive;
            _powerBtn.gameObject.SetActive(myTurn && _legal.Any(a => a.Kind == ActionKind.HeroPower));
            _attackBtn.interactable = myTurn && _legal.Any(a => a.Kind == ActionKind.Attack);
            _endBtn.interactable = myTurn;
            _cancelBtn.gameObject.SetActive(_targeting != null && myTurn);

            UpdateHint(myTurn);
            RefreshLog();
            RebuildPendingButtons();

            if (_e.GameOver)
            {
                if (!_gameOverShown) { _gameOverShown = true; ShowGameOverModal(); }
            }
            else if (_e.Pending != null && _e.Pending.Seat == _me && _e.Pending.Kind != ChoiceKind.DeathrattleAttack)
            {
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
                AddPendingButton("用侍卫挡刀（保住【" + pc.DrawnCard.Type.Name() + "】）",
                    () => { _e.SubmitGuardChoice(true); Refresh(); }, new Color(0.8f, 0.55f, 0.15f));
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
        }

        void AddPendingButton(string label, UnityAction act, Color color)
        {
            var btn = MakeButton(_actionBar, label, act, color, 18);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            btn.transform.SetSiblingIndex(2 + _pendingBtns.Count);
            _pendingBtns.Add(btn.gameObject);
        }

        void UpdateHint(bool myTurn)
        {
            if (_e.GameOver) { _hintText.text = ""; return; }
            if (_e.Pending != null) { _hintText.text = "请在上方操作栏或弹窗中作出抉择。"; return; }
            if (!myTurn) { _hintText.text = "对手行动中……"; return; }
            if (_targeting != null) { _hintText.text = "点击上方一名对手作为目标。"; return; }
            var spec = _e.HeroOf(_me);
            _hintText.text =
                "轮到你行动（MP " + _e.Players[_me].MP + " 点）：\n" +
                "· 点亮的牌可点击发动技能（耗蓝）\n" +
                "· " + spec.Desc + "\n" +
                (_e.BasicAttackUsed ? "（本回合基础攻击已用）\n" : "") +
                "· 行动完毕后点【结束回合】";
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
            if (Input.GetKeyDown(KeyCode.Escape)) OnQuitClicked();
        }

        void OnQuitClicked()
        {
            // Application.Quit 只在打包后的exe里生效，编辑器Play模式下确认后不会真的关（正常现象）
            _modalSticky = true;
            ShowModalBase("退出游戏", "确定要退出吗？当前对局不会保存。");
            AddModalTextButton("退出", () => Application.Quit());
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

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
                string info;
                if (p.Alive)
                {
                    info = "【" + p.HeroName + "】 HP " + p.HP + "/" + p.MaxHP +
                           "\n手牌: " + p.Hand.Count + " 张";
                    if (_e.BribedBy(_me) == p.Seat) info += "\n（已贿赂，不能攻击你）";
                    if (_e.HasArmGuard(p.Seat)) info += "\n（带刀侍卫）";
                    if (_e.IsSilenced(p.Seat)) info += "\n（沉默中）";
                    if (_e.HiddenCount(p.Seat) > 0) info += "\n（藏了" + _e.HiddenCount(p.Seat) + "张）";
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

        // ==================================================================
        // 手牌区
        // ==================================================================

        static readonly CardType[] DisplayOrder =
        {
            CardType.Hero, CardType.Arsenal, CardType.GrandMinister, CardType.Witch, CardType.ArmedGuard,
            CardType.Exorcist, CardType.Police, CardType.Priest, CardType.Warden,
            CardType.Minister, CardType.Undead, CardType.Guard, CardType.Maid
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
                MakeCardView(card, _handArea, SubLabel(card), usable, myTurn, 124,
                    () => OnCardClicked(card));
            }
        }

        Button MakeCardView(Card c, Transform parent, string caption, bool highlighted, bool interactable, float width, UnityAction onClick)
        {
            var go = UIObj("Card_" + c.Id, parent);
            AddImage(go, highlighted ? Lighten(ColorOf(c.Type), 1.25f) : ColorOf(c.Type));
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            string name = c.Type == CardType.Hero ? _e.Players[_me].HeroName : c.Type.Name();
            AddText(txtGO, name + (string.IsNullOrEmpty(caption) ? "" : "\n" + caption),
                19, TextColorOf(c.Type), TextAnchor.MiddleCenter);

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
                case CardType.Hero:     return "永不离手";
                case CardType.Undead:   return "免疫攻击";
                case CardType.Exorcist: return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·可捉亡灵";
                case CardType.Police:   return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·可扣押";
                case CardType.Priest:   return "亡语:回收";
                case CardType.Guard:    return "可挡一刀";
                case CardType.Arsenal:  return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·追加攻击";
                case CardType.Witch:
                    return (c.PoisonUsed ? "毒✗" : "毒2蓝✓") + " " + (c.AntidoteUsed ? "药✗" : "药1蓝✓");
                case CardType.ArmedGuard: return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·亡语布防";
                case CardType.Minister: return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·贿赂结盟";
                case CardType.GrandMinister: return c.UsedSkillThisTurn ? "本回合已用" : "2蓝·勒索牌型";
                case CardType.Warden:   return c.UsedSkillThisTurn ? "本回合已用" : "1蓝·藏一张牌";
                case CardType.Maid:     return "免疫军火库";
                default: return "";
            }
        }

        static Color ColorOf(CardType t)
        {
            switch (t)
            {
                case CardType.Hero:         return new Color(0.75f, 0.6f, 0.15f);
                case CardType.Undead:       return new Color(0.32f, 0.2f, 0.5f);
                case CardType.Exorcist:     return new Color(0.9f, 0.75f, 0.25f);
                case CardType.Police:       return new Color(0.22f, 0.5f, 0.85f);
                case CardType.Priest:       return new Color(0.92f, 0.9f, 0.78f);
                case CardType.Guard:        return new Color(0.5f, 0.36f, 0.26f);
                case CardType.Witch:        return new Color(0.6f, 0.28f, 0.68f);
                case CardType.Arsenal:      return new Color(0.85f, 0.28f, 0.22f);
                case CardType.ArmedGuard:   return new Color(0.7f, 0.45f, 0.15f);
                case CardType.Minister:     return new Color(0.25f, 0.55f, 0.45f);
                case CardType.GrandMinister: return new Color(0.45f, 0.3f, 0.6f);
                case CardType.Warden:       return new Color(0.35f, 0.45f, 0.55f);
                case CardType.Maid:         return new Color(0.85f, 0.7f, 0.75f);
                default:                    return Color.gray;
            }
        }

        static Color TextColorOf(CardType t)
        {
            switch (t)
            {
                case CardType.Exorcist:
                case CardType.Priest:
                case CardType.Maid:
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
        // 玩家操作
        // ==================================================================

        void OnAttackClicked()
        {
            EnterTargeting(_legal.Where(a => a.Kind == ActionKind.Attack).ToList());
        }

        void OnPowerClicked()
        {
            var act = _legal.FirstOrDefault(a => a.Kind == ActionKind.HeroPower);
            if (act != null) { _e.ApplyAction(act); Refresh(); }
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
            _e.ApplyAction(acts[0]);
            Refresh();
        }

        void OnCardClicked(Card c)
        {
            if (_e.GameOver || _e.Pending != null || _e.Current != _me) return;
            if (c.Type == CardType.Hero) { ShowHeroInfo(); return; }

            var acts = _legal.Where(a => a.SourceCardId == c.Id).ToList();
            if (acts.Count == 0)
            {
                _modalSticky = true;
                ShowModalBase(c.Type.Name(), PassiveDesc(c.Type));
                AddModalTextButton("知道了", () => { _modalSticky = false; Refresh(); });
                return;
            }

            switch (c.Type)
            {
                case CardType.Exorcist:
                case CardType.Police:
                case CardType.Arsenal:
                case CardType.Minister:
                    EnterTargeting(acts);
                    break;
                case CardType.Witch:
                    ShowWitchModal(acts);
                    break;
                case CardType.GrandMinister:
                    ShowExtortModal(acts);
                    break;
                case CardType.Warden:
                    ShowHideModal(acts);
                    break;
                case CardType.ArmedGuard:
                    _e.ApplyAction(acts[0]);
                    Refresh();
                    break;
            }
        }

        string PassiveDesc(CardType t)
        {
            switch (t)
            {
                case CardType.Hero: return _e.HeroOf(_me).Desc + "\n英雄牌永不离开手牌，被打中扣1点HP。";
                case CardType.Undead: return "被动：免疫攻击，被抽到时回到手中。";
                case CardType.Priest: return "被动：被抽进弃牌堆时触发亡语，从弃牌堆回收一张角色牌（不能回收牧师和英雄）。";
                case CardType.Guard: return "被动：被攻击时可以选择挡刀，代替被抽中的牌进弃牌堆。";
                case CardType.Maid: return "被动：免疫军火库的追加攻击（普攻和毒药照常吃）。";
                default: return "现在用不了（蓝不够或本回合已用过）。";
            }
        }

        void ShowHeroInfo()
        {
            _modalSticky = true;
            var spec = _e.HeroOf(_me);
            ShowModalBase("英雄【" + _e.Players[_me].HeroName + "】",
                spec.Desc + "\nHP " + _e.Players[_me].HP + "/" + _e.Players[_me].MaxHP +
                "   MP " + _e.Players[_me].MP + "/" + _e.Players[_me].MaxMP +
                "\n英雄牌永不离开手牌，被打中扣1点HP。");
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
                AddModalTextButton("毒药（2蓝）：追加一次攻击", () => { _modalSticky = false; HideModal(); EnterTargeting(poi); });
            var ant = acts.FirstOrDefault(a => a.Kind == ActionKind.Antidote);
            if (ant != null)
                AddModalTextButton("解药（1蓝）：回收一张角色牌", () => { _modalSticky = false; HideModal(); _e.ApplyAction(ant); Refresh(); });
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void ShowExtortModal(List<GameAction> acts)
        {
            _modalSticky = true;
            ShowModalBase("军机大臣 · 勒索（2蓝）", "选择牌型：获得其他所有玩家手牌中的这种牌。选【英雄】= 所有对手各扣1点HP。");
            var list = acts.ToList();
            int half = (list.Count + 1) / 2;
            foreach (var a in list.Take(half)) AddTypeChip(_modalBtns2, a);
            foreach (var a in list.Skip(half)) AddTypeChip(_modalBtns, a);
            AddModalTextButton("取消", () => { _modalSticky = false; Refresh(); });
        }

        void AddTypeChip(Transform row, GameAction act)
        {
            var go = UIObj("Type_" + act.TargetType, row);
            AddImage(go, ColorOf(act.TargetType));
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 118;
            var btn = go.AddComponent<Button>();
            var a = act;
            btn.onClick.AddListener(() => { _modalSticky = false; HideModal(); _e.ApplyAction(a); Refresh(); });
            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, act.TargetType.Name(), 18, TextColorOf(act.TargetType), TextAnchor.MiddleCenter);
        }

        void ShowHideModal(List<GameAction> acts)
        {
            _modalSticky = true;
            ShowModalBase("守卫 · 藏牌（1蓝）",
                "藏起的牌到你的下回合开始前不会被攻击/扣押/勒索碰到。\n不能藏英雄牌，不能连续两回合藏同一张。");
            var shown = new HashSet<int>();
            foreach (var a in acts)
            {
                if (!shown.Add(a.SecondCardId)) continue;
                var act = a;
                var hide = _e.Players[_me].Hand.First(c => c.Id == act.SecondCardId);
                AddModalCard(hide, "藏起", () => { _modalSticky = false; HideModal(); _e.ApplyAction(act); Refresh(); });
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
                ShowModalBase("遭遇攻击！", _e.Players[pc.AttackerSeat].Name + " 攻击了你，随机抽中了下面这张牌。\n点被抽中的牌 = 承担结果；点侍卫 = 挡刀。");
                AddModalCard(pc.DrawnCard, "被抽中了", () => { _e.SubmitGuardChoice(false); Refresh(); });
                var guard = _e.Players[_me].Hand.FirstOrDefault(c => c.Type == CardType.Guard);
                if (guard != null)
                    AddModalCard(guard, "挡刀", () => { _e.SubmitGuardChoice(true); Refresh(); });
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
        }

        void ShowGameOverModal()
        {
            _modalSticky = true;
            bool win = _e.WinnerSeat == _me;
            ShowModalBase(win ? "你赢了！" : "对局结束",
                (win ? "你是最后的存活者！" : "胜者：" + _e.Players[_e.WinnerSeat].Name) +
                "\n共进行了 " + _e.TurnNumber + " 轮。");
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
            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            return btn;
        }
    }
}
