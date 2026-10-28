using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SandKill.Unity
{
    /// <summary>
    /// 主菜单（系列入口）：选择游戏模式、对手数量、退出。
    /// 目前只有"初代"可玩；二代/三代为占位按钮（敬请期待），
    /// 未来做新模式时：点亮对应按钮 + 在 GameBootstrap.StartMode 里接入新模式规则。
    /// 界面由代码生成，与 GameUI 同一套思路（底图当爹、文字当儿子）。
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        GameBootstrap _boot;
        Font _font;
        Button _ai2Btn, _ai3Btn;

        public void Init(GameBootstrap boot)
        {
            _boot = boot;
            _font = GameUI.LoadFont();
            GameUI.EnsureEventSystem();
            Build();
        }

        // ------------------------------------------------------------------
        // 界面搭建
        // ------------------------------------------------------------------

        void Build()
        {
            var canvasGO = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            AddImage(bg, new Color(0.09f, 0.1f, 0.13f));

            // 标题
            var title = UIObj("Title", root);
            SetRect(title, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 230), new Vector2(400, 330));
            AddText(title, "3 D 杀", 96, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter);

            var sub = UIObj("Subtitle", root);
            SetRect(sub, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 185), new Vector2(400, 225));
            AddText(sub, "体锻课时代的物理卡牌 · 数字复刻", 22, new Color(0.6f, 0.65f, 0.7f), TextAnchor.MiddleCenter);

            // 模式按钮
            MakeButton(root, "初代 · 经典模式", () => _boot.StartMode(0),
                new Color(0.85f, 0.6f, 0.18f), 28, -230, 40, 230, 108);
            var desc1 = UIObj("Desc1", root);
            SetRect(desc1, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 4), new Vector2(300, 34));
            AddText(desc1, "最初的规则：一手好牌与神抽，纯粹的消耗战（可玩）", 16, new Color(0.55f, 0.6f, 0.65f), TextAnchor.MiddleCenter);

            MakeButton(root, "二代 · 天赋觉醒", () => _boot.StartMode(1),
                new Color(0.55f, 0.45f, 0.75f), 28, -230, -78, 230, -10);
            var desc2 = UIObj("Desc2", root);
            SetRect(desc2, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -114), new Vector2(300, -84));
            AddText(desc2, "随机英雄 · HP/MP系统 · 新牌五张 · 无工人（可玩，固定3人局）", 16, new Color(0.55f, 0.6f, 0.65f), TextAnchor.MiddleCenter);

            var btn3 = MakeButton(root, "三代 · ???", null,
                new Color(0.3f, 0.32f, 0.38f), 28, -230, -196, 230, -128);
            btn3.interactable = false;
            var desc3 = UIObj("Desc3", root);
            SetRect(desc3, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -232), new Vector2(300, -202));
            AddText(desc3, "更深的博弈（敬请期待）", 16, new Color(0.45f, 0.48f, 0.52f), TextAnchor.MiddleCenter);

            // 对手数量选择
            var aiLabel = UIObj("AILabel", root);
            SetRect(aiLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-230, -300), new Vector2(-30, -252));
            AddText(aiLabel, "对手数量", 20, new Color(0.8f, 0.82f, 0.85f), TextAnchor.MiddleRight);

            _ai2Btn = MakeButton(root, "2 个 AI", () => SetAICount(2),
                Color.clear, 20, -20, -300, 180, -252);
            _ai3Btn = MakeButton(root, "3 个 AI", () => SetAICount(3),
                Color.clear, 20, 190, -300, 390, -252);
            RefreshAIToggle();

            // 退出
            MakeButton(root, "退出游戏", () => Application.Quit(),
                new Color(0.55f, 0.3f, 0.3f), 20, -100, -400, 100, -352);
        }

        void SetAICount(int n)
        {
            _boot.AICount = n;
            RefreshAIToggle();
        }

        void RefreshAIToggle()
        {
            bool two = _boot.AICount == 2;
            _ai2Btn.GetComponent<Image>().color = two ? new Color(0.3f, 0.55f, 0.85f) : new Color(0.25f, 0.27f, 0.33f);
            _ai3Btn.GetComponent<Image>().color = two ? new Color(0.25f, 0.27f, 0.33f) : new Color(0.3f, 0.55f, 0.85f);
        }

        // ------------------------------------------------------------------
        // 基础零件（与 GameUI 同款：底图当爹、文字当儿子）
        // ------------------------------------------------------------------

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

        /// <summary>居中锚点按钮：offMin/offMax 是相对屏幕中心的偏移。</summary>
        Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int fontSize,
            float xMin, float yMin, float xMax, float yMax)
        {
            var go = UIObj("Btn_" + label, parent);
            SetRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(xMin, yMin), new Vector2(xMax, yMax));
            AddImage(go, bg == Color.clear ? new Color(0.25f, 0.27f, 0.33f) : bg);
            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            return btn;
        }
    }
}
