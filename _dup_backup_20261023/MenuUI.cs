using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SandKill.Unity
{
    /// <summary>
    /// 主菜单（系列入口）：选择游戏模式、对手数量、退出。
    /// 未来做新模式时：点亮对应按钮 + 在 GameBootstrap.StartMode 里接入新模式规则。
    /// 界面由代码生成，底图当爹、文字当儿子。
    ///
    /// 2026-09 重构：零件全部来自 UIKit。本类的 MakeButton 是个"薄包装"——
    /// 菜单按钮比通用按钮多一步"按中心锚点摆放"，差异留在这里，核心逻辑在 UIKit 一处。
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        GameBootstrap _boot;
        Font _font;
        Button _ai2Btn, _ai3Btn;

        public void Init(GameBootstrap boot)
        {
            _boot = boot;
            _font = UIKit.LoadFont();
            UIKit.EnsureEventSystem();
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

            var bg = UIKit.UIObj("Background", root);
            UIKit.SetRect(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UIKit.AddImage(bg, new Color(0.09f, 0.1f, 0.13f));

            // 标题
            var title = UIKit.UIObj("Title", root);
            UIKit.SetRect(title, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 230), new Vector2(400, 330));
            UIKit.AddText(title, "3 D 杀", 96, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter, _font);

            var sub = UIKit.UIObj("Subtitle", root);
            UIKit.SetRect(sub, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 185), new Vector2(400, 225));
            UIKit.AddText(sub, "体锻课时代的物理卡牌 · 数字复刻", 22, new Color(0.6f, 0.65f, 0.7f), TextAnchor.MiddleCenter, _font);

            // 模式按钮
            MakeButton(root, "初代 · 经典模式", () => _boot.StartMode(0),
                new Color(0.85f, 0.6f, 0.18f), 28, -230, 40, 230, 108);
            var desc1 = UIKit.UIObj("Desc1", root);
            UIKit.SetRect(desc1, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 4), new Vector2(300, 34));
            UIKit.AddText(desc1, "最初的规则：一手好牌与神抽，纯粹的消耗战（可玩）", 16, new Color(0.55f, 0.6f, 0.65f), TextAnchor.MiddleCenter, _font);

            MakeButton(root, "二代 · 英雄登场", () => _boot.StartMode(1),
                new Color(0.55f, 0.45f, 0.75f), 28, -230, -78, 230, -10);
            var desc2 = UIKit.UIObj("Desc2", root);
            UIKit.SetRect(desc2, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -114), new Vector2(300, -84));
            UIKit.AddText(desc2, "随机英雄 · HP/MP系统 · 合成牌 · 英雄升级（6消灭升2级得护盾、8消灭升3级回血）（可玩，支持 3/4 人局）", 16, new Color(0.55f, 0.6f, 0.65f), TextAnchor.MiddleCenter, _font);

            // 三代·法术迸发（2026-10-21 上线）：2代全部规则 + 法术牌/技能点/learn/彗星反制
            MakeButton(root, "三代 · 法术迸发", () => _boot.StartMode(2),
                new Color(0.3f, 0.55f, 0.65f), 28, -230, -196, 230, -128);
            var desc3 = UIKit.UIObj("Desc3", root);
            UIKit.SetRect(desc3, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -232), new Vector2(300, -202));
            UIKit.AddText(desc3, "2代规则 + 6种法术牌 · 升级得技能点 · learn 研习 · 彗星反制（可玩，支持 3/4 人局）", 16, new Color(0.55f, 0.6f, 0.65f), TextAnchor.MiddleCenter, _font);

            // 对手数量选择
            var aiLabel = UIKit.UIObj("AILabel", root);
            UIKit.SetRect(aiLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-230, -300), new Vector2(-30, -252));
            UIKit.AddText(aiLabel, "对手数量", 20, new Color(0.8f, 0.82f, 0.85f), TextAnchor.MiddleRight, _font);

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
        // 薄包装：菜单按钮 = UIKit 通用按钮 + 中心锚点定位
        // ------------------------------------------------------------------

        /// <summary>居中锚点按钮：offMin/offMax 是相对屏幕中心的偏移。</summary>
        Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int fontSize,
            float xMin, float yMin, float xMax, float yMax)
        {
            var btn = UIKit.MakeButton(parent, label, onClick, bg, fontSize, _font);
            UIKit.SetRect(btn.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(xMin, yMin), new Vector2(xMax, yMax));
            return btn;
        }
    }
}
