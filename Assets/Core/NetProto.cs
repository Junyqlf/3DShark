using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 联机协议（2026-10-20 阶段①，用户与朋友局域网真人测试用）。纯数据 POCO + 字符串编解码，
    /// 不引用 Unity，可被 NGO/FastJSON/自定义 Socket 任意网络层搬运。
    /// 架构 = 主机权威：主机跑完整 GameEngine2（看得见所有手牌），客户端只收本文件的公开快照。
    /// 信息隐藏承诺：对手的手牌内容（牌 Id/牌型/动态状态）绝不进入快照——只有张数。
    /// </summary>

    // ==================== 1. 行动编解码 ====================

    /// <summary>GameAction ↔ 字符串。格式 "K=Attack;T=2;S=12;TT=Witch;SC=5"（键固定，值缺省可省略）。
    /// 严格解析：未知键/非法枚举抛 FormatException——网络脏数据不允许被默默应用。</summary>
    public static class ActionCodec
    {
        public static string Encode(GameAction a)
        {
            var parts = new List<string> { "K=" + a.Kind };
            if (a.TargetSeat >= 0) parts.Add("T=" + a.TargetSeat);
            if (a.SourceCardId >= 0) parts.Add("S=" + a.SourceCardId);
            if (a.TargetType != 0) parts.Add("TT=" + a.TargetType);
            if (a.SecondCardId >= 0) parts.Add("SC=" + a.SecondCardId);
            return string.Join(";", parts.ToArray());
        }

        public static GameAction Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new FormatException("空行动串");
            var a = new GameAction();
            foreach (var seg in s.Split(';'))
            {
                int eq = seg.IndexOf('=');
                if (eq <= 0) throw new FormatException("行动段无键值对: " + seg);
                string key = seg.Substring(0, eq), val = seg.Substring(eq + 1);
                switch (key)
                {
                    case "K": a.Kind = ParseEnum<ActionKind>(val); break;
                    case "T": a.TargetSeat = int.Parse(val); break;
                    case "S": a.SourceCardId = int.Parse(val); break;
                    case "TT": a.TargetType = ParseEnum<CardType>(val); break;
                    case "SC": a.SecondCardId = int.Parse(val); break;
                    default: throw new FormatException("未知行动键: " + key);
                }
            }
            return a;
        }

        /// <summary>枚举严格解析：非法值统一抛 FormatException（ArgumentException 是 .NET 实现细节，不该泄漏给网络层）。</summary>
        internal static T ParseEnum<T>(string val)
        {
            try { return (T)Enum.Parse(typeof(T), val); }
            catch (ArgumentException ex) { throw new FormatException("非法枚举值: " + val, ex); }
        }
    }

    // ==================== 2. 卡牌实例编解码 ====================

    /// <summary>Card（含全部动态状态）↔ 字符串。仅用于"观者自己的手牌"与"抉择请求里的候选牌"——
    /// 对手手牌绝不走这条通道（见 PublicSnapshot 的信息隐藏承诺）。</summary>
    public static class CardCodec
    {
        public static string Encode(Card c)
        {
            var parts = new List<string>
            {
                "Id=" + c.Id, "Ty=" + c.Type,
                "U=" + (c.UsedSkillThisTurn ? 1 : 0), "UT=" + c.UsedSkillTurn,
                "P=" + (c.PoisonUsed ? 1 : 0), "A=" + (c.AntidoteUsed ? 1 : 0),
                "E=" + (c.ExtortUsed ? 1 : 0), "D=" + c.DiedStamp,
                "G=" + c.GuardProtectTurn, "LG=" + c.LastGuardedTurn, "Ap=" + c.AppleTimer,
                "SH=" + c.SynthHP, "RV=" + (c.ReviveUsed ? 1 : 0), "PR=" + (c.PrayerUsed ? 1 : 0),
                "PD=" + c.PrayerDueTurn,
                "B=" + (c.BombActive ? 1 : 0), "BP=" + c.BombPlanter, "BD=" + c.BombDueTurn,
                "SS=" + c.SeizeStamp, "SB=" + c.SealedBy
            };
            return string.Join(";", parts.ToArray());
        }

        public static Card Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new FormatException("空卡牌串");
            var map = new Dictionary<string, string>();
            foreach (var seg in s.Split(';'))
            {
                int eq = seg.IndexOf('=');
                if (eq <= 0) throw new FormatException("卡牌段无键值对: " + seg);
                map[seg.Substring(0, eq)] = seg.Substring(eq + 1);
            }
            try
            {
                var c = new Card(int.Parse(map["Id"]), ActionCodec.ParseEnum<CardType>(map["Ty"]));
                c.UsedSkillThisTurn = map["U"] == "1";
                c.UsedSkillTurn = int.Parse(map["UT"]);
                c.PoisonUsed = map["P"] == "1";
                c.AntidoteUsed = map["A"] == "1";
                c.ExtortUsed = map["E"] == "1";
                c.DiedStamp = int.Parse(map["D"]);
                c.GuardProtectTurn = int.Parse(map["G"]);
                c.LastGuardedTurn = int.Parse(map["LG"]);
                c.AppleTimer = int.Parse(map["Ap"]);
                c.SynthHP = int.Parse(map["SH"]);
                c.ReviveUsed = map["RV"] == "1";
                c.PrayerUsed = map["PR"] == "1";
                c.PrayerDueTurn = int.Parse(map["PD"]);
                c.BombActive = map["B"] == "1";
                c.BombPlanter = int.Parse(map["BP"]);
                c.BombDueTurn = int.Parse(map["BD"]);
                c.SeizeStamp = int.Parse(map["SS"]);
                c.SealedBy = int.Parse(map["SB"]);
                return c;
            }
            catch (KeyNotFoundException ex) { throw new FormatException("卡牌串缺字段: " + ex.Message); }
        }

        /// <summary>两张牌全部字段相等（测试与对账用）。</summary>
        public static bool Same(Card a, Card b)
        {
            return a.Id == b.Id && a.Type == b.Type
                && a.UsedSkillThisTurn == b.UsedSkillThisTurn && a.UsedSkillTurn == b.UsedSkillTurn
                && a.PoisonUsed == b.PoisonUsed && a.AntidoteUsed == b.AntidoteUsed
                && a.ExtortUsed == b.ExtortUsed && a.DiedStamp == b.DiedStamp
                && a.GuardProtectTurn == b.GuardProtectTurn && a.LastGuardedTurn == b.LastGuardedTurn
                && a.AppleTimer == b.AppleTimer && a.SynthHP == b.SynthHP
                && a.ReviveUsed == b.ReviveUsed && a.PrayerUsed == b.PrayerUsed
                && a.PrayerDueTurn == b.PrayerDueTurn
                && a.BombActive == b.BombActive && a.BombPlanter == b.BombPlanter
                && a.BombDueTurn == b.BombDueTurn && a.SeizeStamp == b.SeizeStamp
                && a.SealedBy == b.SealedBy;
        }
    }

    // ==================== 3. 抉择请求/应答 ====================

    /// <summary>PendingChoice 的线上形态：卡牌全部走 CardCodec 字符串，POCO 可被任意序列化器搬运。
    /// 只有"该抉择的归属座位"才应收到本对象（见 PublicSnapshot.Build 的闸门）。</summary>
    [Serializable]
    public class PendingWire
    {
        public ChoiceKind Kind;
        public int Seat;
        public int AttackerSeat = -1;
        public ActionKind AttackKind;
        public string Reason = "";
        public string DrawnEnc;                        // GuardIntercept：被抽中的牌（可能为 null）
        public List<string> InterceptorsEnc = new List<string>();   // GuardIntercept：可挡刀的牌
        public List<string> OptionsEnc = new List<string>();        // PickFromDiscard：候选牌

        public static PendingWire From(PendingChoice pc)
        {
            var w = new PendingWire
            {
                Kind = pc.Kind, Seat = pc.Seat, AttackerSeat = pc.AttackerSeat,
                AttackKind = pc.AttackKind, Reason = pc.Reason ?? ""
            };
            if (pc.DrawnCard != null) w.DrawnEnc = CardCodec.Encode(pc.DrawnCard);
            if (pc.Interceptors != null) w.InterceptorsEnc = pc.Interceptors.Select(CardCodec.Encode).ToList();
            if (pc.Options != null) w.OptionsEnc = pc.Options.Select(CardCodec.Encode).ToList();
            return w;
        }

        /// <summary>还原为引擎的 PendingChoice（全新 Card 实例，仅供展示与提交，不回灌引擎状态）。</summary>
        public PendingChoice ToPending()
        {
            var pc = new PendingChoice
            {
                Kind = Kind, Seat = Seat, AttackerSeat = AttackerSeat,
                AttackKind = AttackKind, Reason = Reason
            };
            if (DrawnEnc != null) pc.DrawnCard = CardCodec.Decode(DrawnEnc);
            pc.Interceptors = InterceptorsEnc.Select(CardCodec.Decode).ToList();
            pc.Options = OptionsEnc.Select(CardCodec.Decode).ToList();
            return pc;
        }
    }

    /// <summary>客户端对抉择的应答。编码："G:1:cardId"=挡刀用某牌 / "G:0"=不挡 / "P:cardId"=拿回某牌 / "D:seat"=亡语攻击某座位。
    /// 主机收到后分别转调 SubmitGuardChoice / SubmitDiscardPick / SubmitDeathrattleTarget。</summary>
    [Serializable]
    public class ChoiceResponse
    {
        public bool IsGuard;        // true=挡刀应答（UseGuard/CardId 有效）；false=Pick 或 Deathrattle
        public bool UseGuard;
        public int CardId = -1;
        public int TargetSeat = -1;

        public string Encode()
        {
            if (IsGuard) return UseGuard ? "G:1:" + CardId : "G:0";
            if (TargetSeat >= 0) return "D:" + TargetSeat;
            return "P:" + CardId;
        }

        public static ChoiceResponse Decode(string s)
        {
            var parts = (s ?? "").Split(':');
            if (parts.Length < 2) throw new FormatException("非法抉择应答: " + s);
            var r = new ChoiceResponse();
            switch (parts[0])
            {
                case "G":
                    r.IsGuard = true;
                    r.UseGuard = parts[1] == "1";
                    if (r.UseGuard)
                    {
                        if (parts.Length < 3) throw new FormatException("挡刀应答缺牌Id: " + s);
                        r.CardId = int.Parse(parts[2]);
                    }
                    break;
                case "P":
                    r.CardId = int.Parse(parts[1]);
                    break;
                case "D":
                    r.TargetSeat = int.Parse(parts[1]);
                    break;
                default:
                    throw new FormatException("未知抉择应答类型: " + s);
            }
            return r;
        }
    }

    // ==================== 4. 公开信息快照（信息隐藏核心） ====================

    /// <summary>单个玩家的公开状态。HandEnc 仅观者自己非空；对手恒为空列表（只有 HandCount）。</summary>
    [Serializable]
    public class PlayerPub
    {
        public int Seat;
        public string Name = "";
        public string HeroName = "";
        public bool IsHuman;
        public bool Alive;
        public int HP, MaxHP, MP, MaxMP;
        public int HandCount;
        public int ShieldCharges, ShieldExpireOwnTurn;
        public int HeroLevel, EXP;             // 消灭计数与升级是公开信息（引擎日志本就播报）
        public bool Silenced;
        public int MulliganTickets;
        public List<string> HandEnc = new List<string>();   // 只有自己：CardCodec 编码的完整手牌
    }

    /// <summary>发给某一座位客户端的完整公开视图。
    /// 信息隐藏承诺：对手 HandEnc 为空、Pending 仅归属者可见、Legal 仅观者自己的合法行动。</summary>
    [Serializable]
    public class PublicSnapshot
    {
        public int Viewer;
        public int Current;
        public int TurnNumber;
        public bool GameOver;
        public int WinnerSeat = -1;
        public bool InMulligan;
        public int DiscardCount, SideDeckCount;
        public List<PlayerPub> Players = new List<PlayerPub>();
        public List<string> History = new List<string>();   // 公开日志（与主机 UI 同一份可见性）
        public PendingWire Pending;                          // 仅当 Pending.Seat==Viewer 时非空
        public List<string> Legal = new List<string>();      // 观者自己的合法行动（ActionCodec 编码）

        /// <summary>从主机引擎构建某个座位的客户端视图。本方法是信息隐藏的唯一关口：
        /// 对手的手牌内容在这里被结构性丢弃（不编码、不出现在任何字段里）。</summary>
        public static PublicSnapshot Build(GameEngine2 e, int viewer)
        {
            var snap = new PublicSnapshot
            {
                Viewer = viewer,
                Current = e.Current,
                TurnNumber = e.TurnNumber,
                GameOver = e.GameOver,
                WinnerSeat = e.WinnerSeat,
                InMulligan = e.InMulligan,
                DiscardCount = e.Discard.Count,
                SideDeckCount = e.SideDeck.Count
            };
            foreach (var p in e.Players)
            {
                var pub = new PlayerPub
                {
                    Seat = p.Seat, Name = p.Name, HeroName = p.HeroName ?? "",
                    IsHuman = p.IsHuman, Alive = p.Alive,
                    HP = p.HP, MaxHP = p.MaxHP, MP = p.MP, MaxMP = p.MaxMP,
                    HandCount = p.Hand.Count,
                    ShieldCharges = p.ShieldCharges, ShieldExpireOwnTurn = p.ShieldExpireOwnTurn,
                    HeroLevel = p.HeroLevel, EXP = p.EXP,
                    Silenced = e.IsSilenced(p.Seat),
                    MulliganTickets = e.MulliganTickets != null ? e.MulliganTickets[p.Seat] : 0
                };
                if (p.Seat == viewer)
                    pub.HandEnc = p.Hand.Select(CardCodec.Encode).ToList();
                // else：对手 HandEnc 保持空——隐藏信息的不变量在此兑现
                snap.Players.Add(pub);
            }
            snap.History = e.History.ToList();
            if (e.Pending != null && e.Pending.Seat == viewer)
                snap.Pending = PendingWire.From(e.Pending);   // 别人的抉择请求不广播内容
            if (!e.GameOver && e.Pending == null)
                snap.Legal = e.GetLegalActions(viewer).Select(ActionCodec.Encode).ToList();
            return snap;
        }

        /// <summary>解码自己的合法行动（客户端 UI 点牌判定用）。</summary>
        public List<GameAction> DecodedLegal()
        {
            return Legal.Select(ActionCodec.Decode).ToList();
        }
    }
}
