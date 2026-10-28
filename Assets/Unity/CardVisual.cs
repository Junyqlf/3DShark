using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 卡牌外观表：只管"一张牌长什么样"的纯表现层数据（底色、文字色）。
    ///
    /// 分层方式（和 Core 的 CardDefs 分工）：
    ///   CardDefs（Core）= 牌的"身份"：中文名、蓝耗、技能/被动说明——规则相关，引擎也用它；
    ///   CardVisual（本类）= 牌的"长相"：颜色——只有界面关心。
    ///   名字、副标签、说明文字一律从 CardDefs 查；这里只补 CardDefs 没有的视觉数据。
    ///
    /// 手牌副标签的拼接规则（静态归表、动态留代码）：
    ///   主动技：def.SkillCost + "蓝·" + def.SkillTag（如"1蓝·可扣押"；初代界面不带蓝耗前缀）
    ///   被动牌：def.PassiveTag（如"免疫攻击"）
    ///   随对局变化的动态状态（"本回合已用"、女巫"毒✓药✗"）由 UI 查表后自行拼接。
    /// </summary>
    public static class CardVisual
    {
        /// <summary>一张牌的视觉定义。纯数据，没有任何方法。</summary>
        public class Def
        {
            public Color Bg;     // 卡牌底色
            public Color Text;   // 文字颜色（浅色底配深色字，深色底配白字）
            public string Icon;  // 符号图标（Resources/Icons 下的文件名，不含扩展名；null=纯文字维持原状）
        }

        static readonly Color DarkText = new Color(0.15f, 0.15f, 0.18f);

        static readonly Dictionary<CardType, Def> Table = new Dictionary<CardType, Def>
        {
            { CardType.Worker,        new Def { Bg = new Color(0.55f, 0.55f, 0.58f), Text = DarkText, Icon = "hammer" } },
            { CardType.Undead,        new Def { Bg = new Color(0.32f, 0.2f, 0.5f),   Text = Color.white, Icon = "ghost" } },
            { CardType.Exorcist,      new Def { Bg = new Color(0.25f, 0.55f, 0.45f),  Text = Color.white, Icon = "bell" } },
            { CardType.Police,        new Def { Bg = new Color(0.22f, 0.5f, 0.85f),  Text = Color.white, Icon = "police" } },
            { CardType.Priest,        new Def { Bg = new Color(0.92f, 0.9f, 0.78f),  Text = DarkText, Icon = "priest" } },
            { CardType.Guard,         new Def { Bg = new Color(0.5f, 0.36f, 0.26f),  Text = Color.white, Icon = "guard" } },
            { CardType.Witch,         new Def { Bg = new Color(0.6f, 0.28f, 0.68f),  Text = Color.white, Icon = "witch" } },
            { CardType.Arsenal,       new Def { Bg = new Color(0.85f, 0.28f, 0.22f), Text = Color.white, Icon = "arsenal" } },
            { CardType.Hero,          new Def { Bg = new Color(0.75f, 0.6f, 0.15f),  Text = Color.white } },
            { CardType.ArmedGuard,    new Def { Bg = new Color(0.7f, 0.45f, 0.15f),  Text = Color.white, Icon = "dualblade" } },
            { CardType.Minister,      new Def { Bg = new Color(0.9f, 0.75f, 0.25f),  Text = DarkText, Icon = "moneybag" } },
            { CardType.GrandMinister, new Def { Bg = new Color(0.45f, 0.3f, 0.6f),   Text = Color.white, Icon = "extort" } },
            { CardType.Warden,        new Def { Bg = new Color(0.35f, 0.45f, 0.55f), Text = Color.white, Icon = "swordshield" } },
            { CardType.Maid,          new Def { Bg = new Color(0.85f, 0.7f, 0.75f),  Text = DarkText, Icon = "fan" } },
            { CardType.GreenApple,    new Def { Bg = new Color(0.25f, 0.8f, 0.62f),  Text = DarkText, Icon = "apple" } },   // 青色卡背
            { CardType.Terrorist,     new Def { Bg = new Color(0.9f, 0.5f, 0.12f),  Text = Color.white, Icon = "bomb" } },  // 橙色卡背
            // 3代·法术迸发（2026-10-21）：法术牌统一冷色调底，与角色牌区分
            { CardType.Alchemy,       new Def { Bg = new Color(0.35f, 0.6f, 0.75f),  Text = Color.white } },   // 炼金术·青蓝（2026-10-23 退环境，配色保留）
            { CardType.Clarity,       new Def { Bg = new Color(0.55f, 0.8f, 0.9f),   Text = DarkText } },      // 清晰术·冰青（2026-10-23 退环境，配色保留）
            { CardType.Ignite,        new Def { Bg = new Color(0.85f, 0.35f, 0.15f),  Text = Color.white, Icon = "flame" } },    // 点燃·火橙
            { CardType.Thunder,       new Def { Bg = new Color(0.5f, 0.5f, 0.85f),   Text = Color.white, Icon = "lightning" } }, // 雷霆·紫蓝
            { CardType.Comet,         new Def { Bg = new Color(0.2f, 0.22f, 0.35f),  Text = Color.white } },   // 彗星·夜空
            { CardType.Barrier,       new Def { Bg = new Color(0.45f, 0.75f, 0.55f),  Text = DarkText, Icon = "wind" } },     // 风墙（原屏障）·薄荷绿
            { CardType.Glacier,       new Def { Bg = new Color(0.6f, 0.85f, 0.95f),  Text = DarkText, Icon = "snow" } },     // 冰川·冰蓝（2026-10-23）
            { CardType.Heal,          new Def { Bg = new Color(0.55f, 0.9f, 0.6f),   Text = DarkText, Icon = "heal" } },      // 治疗术·嫩绿（2026-10-23）
        };

        /// <summary>查表：这张牌长什么样。没登记的牌给灰色兜底，界面永远能渲染。</summary>
        public static Def Of(CardType t)
        {
            Def def;
            if (Table.TryGetValue(t, out def)) return def;
            return new Def { Bg = Color.gray, Text = Color.white };
        }

        // ---- 符号图标加载（懒加载 + 缓存；PNG 放 Assets/Resources/Icons/，无美术资源用程序生成） ----
        static readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

        /// <summary>这张牌的符号图标 Sprite；没配图标或资源缺失返回 null，界面回落纯文字。</summary>
        public static Sprite IconOf(CardType t)
        {
            string path = Of(t).Icon;
            if (string.IsNullOrEmpty(path)) return null;
            Sprite s;
            if (_iconCache.TryGetValue(path, out s) && s != null) return s;
            var tex = Resources.Load<Texture2D>("Icons/" + path);
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _iconCache[path] = s;
            return s;
        }
    }
}
