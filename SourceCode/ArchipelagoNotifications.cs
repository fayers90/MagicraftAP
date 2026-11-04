
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Archipelago.MultiClient.Net;
using UnityEngine;

namespace MagicraftAP
{
    // Lightweight IMGUI feed. Network threads only update data;
    // Unity GUI runs on the main thread.
    public static class ArchipelagoNotifications
    {
        private sealed class Entry
        {
            public string Item, Player, Kind;
            public bool Incoming;
            public float Start;
        }

        private sealed class Scouted
        {
            public string Item, Player, Kind;
        }

        private static readonly object Gate = new object();

        private static readonly Dictionary<long, Scouted> ScoutCache =
            new Dictionary<long, Scouted>();

        private static readonly HashSet<long> SeenChecks =
            new HashSet<long>();

        private static readonly HashSet<long> PendingChecks =
            new HashSet<long>();

        private static readonly Queue<Entry> Queue =
            new Queue<Entry>();

        private static readonly List<Entry> Active =
            new List<Entry>();

        private static readonly List<Entry> History =
            new List<Entry>();

        private static ArchipelagoSession activeSession;
        private static int receivedBaseline;
        private static bool receivedInitialized;

        private static GUIStyle itemStyle;
        private static GUIStyle detailStyle;
        private static GUIStyle historyStyle;

        private const float Duration = 4f;

        // Notification appearance settings.
        private const int NotificationFontSize = 17;
        private const float NotificationGap = 16f;
        private const float NotificationHeight = 34f;
        private const float NotificationSpacing = 38f;
        private const float BottomOffset = 200f;
        private const float TextPadding = 16f;

        public static void BeginSession(ArchipelagoSession session)
        {
            lock (Gate)
            {
                activeSession = session;

                ScoutCache.Clear();
                SeenChecks.Clear();
                PendingChecks.Clear();
                Queue.Clear();
                Active.Clear();
                History.Clear();

                foreach (long id in session.Locations.AllLocationsChecked)
                    SeenChecks.Add(id);

                receivedInitialized = false;
                receivedBaseline = 0;

                // Establish the initial received-item baseline
                // before showing new arrivals.
                try
                {
                    receivedBaseline = ReadItems(session).Count;
                    receivedInitialized = true;
                }
                catch
                {
                    // First successful poll establishes the baseline.
                }
            }
        }

        public static void EndSession()
        {
            lock (Gate)
            {
                activeSession = null;

                ScoutCache.Clear();
                SeenChecks.Clear();
                PendingChecks.Clear();
                Queue.Clear();
                Active.Clear();
                History.Clear();

                receivedInitialized = false;
            }
        }

        public static void SetScouted(
            long location,
            string item,
            string player,
            string kind)
        {
            lock (Gate)
            {
                ScoutCache[location] = new Scouted
                {
                    Item = item,
                    Player = player,
                    Kind = kind
                };
            }
        }

        private static object Member(object obj, string name)
        {
            if (obj == null)
                return null;

            Type t = obj.GetType();

            return t.GetProperty(
                       name,
                       BindingFlags.Public | BindingFlags.Instance
                   )?.GetValue(obj, null)
                   ??
                   t.GetField(
                       name,
                       BindingFlags.Public | BindingFlags.Instance
                   )?.GetValue(obj);
        }

        private static string StringMember(
            object obj,
            params string[] names)
        {
            foreach (string name in names)
            {
                object value = Member(obj, name);

                if (value != null &&
                    !string.IsNullOrWhiteSpace(Convert.ToString(value)))
                {
                    return Convert.ToString(value);
                }
            }

            return null;
        }

        private static string Kind(object value)
        {
            string flags =
                StringMember(value, "Flags", "ItemFlags") ?? "0";

            long bits;

            if (!long.TryParse(flags, out bits))
            {
                bits = 0;

                if (flags.IndexOf(
                        "Advancement",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    flags.IndexOf(
                        "Progression",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bits |= 1;
                }

                if (flags.IndexOf(
                        "Useful",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bits |= 2;
                }

                if (flags.IndexOf(
                        "Trap",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bits |= 4;
                }
            }

            return (bits & 1) != 0 ? "Progression"
                : (bits & 4) != 0 ? "Trap"
                : (bits & 2) != 0 ? "Useful"
                : "Filler";
        }

        private static string ItemName(
            ArchipelagoSession session,
            object item)
        {
            string direct = StringMember(
                item,
                "ItemName",
                "Name"
            );

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            object idValue = Member(item, "ItemId");

            if (idValue != null)
            {
                foreach (MethodInfo method in
                    session.Items.GetType()
                        .GetMethods()
                        .Where(m => m.Name == "GetItemName"))
                {
                    try
                    {
                        var parameters = method.GetParameters();

                        if (parameters.Length != 1 ||
                            (parameters[0].ParameterType != typeof(long) &&
                             parameters[0].ParameterType != typeof(int)))
                        {
                            continue;
                        }

                        string name = Convert.ToString(
                            method.Invoke(
                                session.Items,
                                new[]
                                {
                                    Convert.ChangeType(
                                        idValue,
                                        parameters[0].ParameterType
                                    )
                                }
                            )
                        );

                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                    catch
                    {
                    }
                }
            }

            return idValue == null
                ? "Archipelago Item"
                : "Item #" + idValue;
        }

        private static List<object> ReadItems(
            ArchipelagoSession session)
        {
            IEnumerable received =
                Member(session.Items, "AllItemsReceived")
                as IEnumerable;

            return received == null
                ? new List<object>()
                : received.Cast<object>().ToList();
        }

        private static void Enqueue(Entry entry)
        {
            Queue.Enqueue(entry);
            History.Add(entry);

            if (History.Count > 50)
                History.RemoveAt(0);
        }

        // Called on Unity's main thread from Plugin.Update.
        // Only server-confirmed checks count as outgoing items.
        public static void Poll(ArchipelagoSession session)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(session, activeSession))
                    return;

                try
                {
                    var received = ReadItems(session);

                    if (!receivedInitialized)
                    {
                        receivedBaseline = received.Count;
                        receivedInitialized = true;
                    }

                    for (int i = receivedBaseline;
                         i < received.Count;
                         i++)
                    {
                        object item = received[i];

                        string name = ItemName(session, item);

                        string sender =
                            ArchipelagoShop.ResolveRecipient(
                                session,
                                item
                            );

                        Enqueue(new Entry
                        {
                            Item = name,
                            Player = sender,
                            Kind = Kind(item),
                            Incoming = true
                        });
                    }

                    receivedBaseline = received.Count;
                }
                catch
                {
                    // Retry on the next frame.
                }

                try
                {
                    foreach (long id in
                        session.Locations.AllLocationsChecked)
                    {
                        if (SeenChecks.Add(id))
                            PendingChecks.Add(id);
                    }

                    foreach (long id in PendingChecks.ToArray())
                    {
                        Scouted info;

                        if (!ScoutCache.TryGetValue(id, out info))
                            continue;

                        PendingChecks.Remove(id);

                        Enqueue(new Entry
                        {
                            Item = info.Item,
                            Player = info.Player,
                            Kind = info.Kind,
                            Incoming = false
                        });
                    }
                }
                catch
                {
                    // Wait for server state to become available.
                }
            }
        }

        private static Color KindColor(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "progression":
                    return new Color(1f, .79f, .42f);

                case "useful":
                    return new Color(.76f, .61f, 1f);

                case "trap":
                    return new Color(1f, .48f, .48f);

                default:
                    return new Color(.67f, .77f, .91f);
            }
        }

        private static void Styles()
        {
            if (itemStyle != null)
                return;

            // All notification segments use the same font size
            // and vertical alignment.
            itemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = NotificationFontSize,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            detailStyle = new GUIStyle(itemStyle);

            historyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
        }

        // Draw a four-direction black outline to keep text
        // readable against bright and dark environments.
        private static void OutlinedLabel(
            Rect rect,
            string text,
            GUIStyle style,
            Color color)
        {
            Color old = GUI.color;

            GUI.color = new Color(
                0f,
                0f,
                0f,
                color.a * .95f
            );

            const float outline = 1.5f;

            GUI.Label(
                new Rect(
                    rect.x - outline,
                    rect.y,
                    rect.width,
                    rect.height
                ),
                text,
                style
            );

            GUI.Label(
                new Rect(
                    rect.x + outline,
                    rect.y,
                    rect.width,
                    rect.height
                ),
                text,
                style
            );

            GUI.Label(
                new Rect(
                    rect.x,
                    rect.y - outline,
                    rect.width,
                    rect.height
                ),
                text,
                style
            );

            GUI.Label(
                new Rect(
                    rect.x,
                    rect.y + outline,
                    rect.width,
                    rect.height
                ),
                text,
                style
            );

            GUI.color = color;
            GUI.Label(rect, text, style);

            GUI.color = old;
        }

        // Only the visual layout has changed.
        public static void Draw()
        {
            Styles();

            lock (Gate)
            {
                float now = Time.unscaledTime;

                Active.RemoveAll(
                    e => now - e.Start >= Duration
                );

                while (Active.Count < 2 && Queue.Count > 0)
                {
                    Entry entry = Queue.Dequeue();
                    entry.Start = now;
                    Active.Add(entry);
                }

                float baseY =
                    Screen.height - BottomOffset;

                // Leave a small margin on either side.
                float availableWidth =
                    Mathf.Max(1f, Screen.width - 48f);

                for (int i = 0; i < Active.Count; i++)
                {
                    Entry entry = Active[i];

                    float alpha = Mathf.Clamp01(
                        (Duration - (now - entry.Start)) / .8f
                    );

                    float y =
                        baseY + i * NotificationSpacing;

                    string item =
                        (entry.Incoming ? "+ " : "") +
                        (entry.Item ?? "Archipelago Item");

                    string detail =
                        (entry.Incoming ? "← " : "→ ") +
                        (entry.Player ?? "Unknown Player");

                    string kind =
                        "[" + (entry.Kind ?? "Filler") + "]";

                    // Measure the actual rendered text.
                    // Extra padding prevents bold glyphs and
                    // outlines from being clipped.
                    float itemWidth =
                        itemStyle.CalcSize(
                            new GUIContent(item)
                        ).x + TextPadding;

                    float detailWidth =
                        detailStyle.CalcSize(
                            new GUIContent(detail)
                        ).x + TextPadding;

                    float kindWidth =
                        detailStyle.CalcSize(
                            new GUIContent(kind)
                        ).x + TextPadding;

                    float totalWidth =
                        itemWidth +
                        detailWidth +
                        kindWidth +
                        NotificationGap * 2f;

                    // Long names use the available screen width.
                    // Scale uniformly only when necessary.
                    float scale = Mathf.Min(
                        1f,
                        availableWidth /
                        Mathf.Max(totalWidth, 1f)
                    );

                    Matrix4x4 oldMatrix = GUI.matrix;

                    if (scale < 1f)
                    {
                        GUI.matrix =
                            Matrix4x4.Scale(
                                new Vector3(
                                    scale,
                                    scale,
                                    1f
                                )
                            ) * oldMatrix;
                    }

                    float x =
                        (Screen.width / scale - totalWidth)
                        * .5f;

                    float drawY = y / scale;

                    Color primary = entry.Incoming
                        ? new Color(.57f, .95f, .69f, alpha)
                        : new Color(1f, .81f, .47f, alpha);

                    Color category =
                        KindColor(entry.Kind);

                    // All three segments share the same height,
                    // baseline and font size.
                    OutlinedLabel(
                        new Rect(
                            x,
                            drawY,
                            itemWidth,
                            NotificationHeight
                        ),
                        item,
                        itemStyle,
                        primary
                    );

                    x += itemWidth + NotificationGap;

                    OutlinedLabel(
                        new Rect(
                            x,
                            drawY,
                            detailWidth,
                            NotificationHeight
                        ),
                        detail,
                        detailStyle,
                        new Color(1f, 1f, 1f, alpha)
                    );

                    x += detailWidth + NotificationGap;

                    OutlinedLabel(
                        new Rect(
                            x,
                            drawY,
                            kindWidth,
                            NotificationHeight
                        ),
                        kind,
                        detailStyle,
                        new Color(
                            category.r,
                            category.g,
                            category.b,
                            alpha
                        )
                    );

                    GUI.matrix = oldMatrix;
                }
            }
        }

        public static void DrawHistory(Rect rect)
        {
            Styles();

            GUI.Label(
                new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    20
                ),
                "Recent Archipelago items (latest 8)",
                detailStyle
            );

            lock (Gate)
            {
                int row = 0;

                for (int i = History.Count - 1;
                     i >= 0 && row < 8;
                     i--, row++)
                {
                    Entry entry = History[i];

                    string text =
                        entry.Item +
                        (entry.Incoming ? " ← " : " → ") +
                        entry.Player +
                        " [" + entry.Kind + "]";

                    GUI.Label(
                        new Rect(
                            rect.x,
                            rect.y + 22 + row * 17,
                            rect.width,
                            17
                        ),
                        text,
                        historyStyle
                    );
                }

                if (History.Count == 0)
                {
                    GUI.Label(
                        new Rect(
                            rect.x,
                            rect.y + 23,
                            rect.width,
                            20
                        ),
                        "No new items this session.",
                        historyStyle
                    );
                }
            }
        }
    }
}
