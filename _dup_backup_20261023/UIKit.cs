using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SandKill.Unity
{
    /// <summary>
    /// 运行时 UI 零件工厂：所有界面类（GameUI / GameUI2 / MenuUI）共用的一套"搭积木"方法。
    ///
    /// 为什么是静态类？
    ///   这些方法不记录任何状态，输出完全由参数决定——做完一个按钮它什么都不记得。
    ///   无状态的工具方法 = 静态类的标准用法：不用 new、不用挂 MonoBehaviour，随时 UIKit.XXX() 调用。
    ///
    /// 唯一的"状态"是字体 Font：原来存在各 UI 类的 _font 字段里，现在改成由调用方当参数传进来。
    /// 这样工具类保持零状态，字体归各自的界面类管理。
    ///
    /// 抽取方法（以后你自己抽别的公共逻辑也照这个流程）：
    ///   1. 把重复的方法原样复制到这里，加 public static；
    ///   2. 类内部互相调用时不用写 UIKit. 前缀（UIObj、SetRect 直接调）；
    ///   3. 用到的实例字段（_font）改成方法参数；
    ///   4. 回到调用方，删掉本地私有方法，调用处加 UIKit. 前缀。
    /// </summary>
    public static class UIKit
    {
        // ------------------------------------------------------------------
        // 字体与事件系统（原 GameUI 里的两个静态方法，天然就该是工具方法）
        // ------------------------------------------------------------------

        /// <summary>加载中文字体。内置字体不含中文字形，优先使用操作系统中文字体。</summary>
        public static Font LoadFont()
        {
            string[] cjk = { "Microsoft YaHei", "SimHei", "PingFang SC",
                             "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei" };
            var f = Font.CreateDynamicFontFromOSFont(cjk, 16);
            if (f != null) return f;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        }

        /// <summary>保证场景里有一个 EventSystem（uGUI 点击事件的总开关），没有就建一个。</summary>
        public static void EnsureEventSystem()
        {
#if UNITY_2022_2_OR_NEWER
            if (Object.FindFirstObjectByType<EventSystem>() == null)
#else
            if (Object.FindObjectOfType<EventSystem>() == null)
#endif
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ------------------------------------------------------------------
        // 基础零件（原来在三个 UI 类里各复制一份的五个方法）
        // ------------------------------------------------------------------

        /// <summary>创建一个挂 RectTransform 的空 UI 节点并挂到 parent 下。</summary>
        public static GameObject UIObj(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>设置锚点与偏移（原样搬运）。</summary>
        public static void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        /// <summary>加一块纯色底图。</summary>
        public static Image AddImage(GameObject go, Color c)
        {
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        /// <summary>加一段文字。font 由调用方传入。</summary>
        public static Text AddText(GameObject go, string content, int size, Color color, TextAnchor anchor, Font font)
        {
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = false;
            return t;
        }

        /// <summary>
        /// 造一个按钮（底图 + Button 组件 + 子物体文字）。
        /// 注意：一个 GameObject 只能有一个 Graphic，所以 Image 在按钮本体、Text 必须放到子物体。
        /// 不在这里 SetRect——布局交给调用方或 LayoutGroup，保持零件通用。
        /// </summary>
        public static Button MakeButton(Transform parent, string label, UnityAction onClick, Color bg, int fontSize, Font font)
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
            if (onClick != null) btn.onClick.AddListener(onClick);

            var txtGO = UIObj("Label", go.transform);
            SetRect(txtGO, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(txtGO, label, fontSize, Color.white, TextAnchor.MiddleCenter, font);
            return btn;
        }

        /// <summary>颜色提亮（原 GameUI/GameUI2 里的 Lighten，做"可用状态高亮"用）。</summary>
        public static Color Lighten(Color c, float k)
        {
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }
    }
}
