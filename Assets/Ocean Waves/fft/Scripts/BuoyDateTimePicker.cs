using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactive on-screen Date & Time picker UI for buoy data.
/// Provides a calendar grid for date selection and a slider for time selection.
/// Integrates with DatabaseManager to fetch data when date/time changes.
/// 
/// Attach to any GameObject. Assign DatabaseManager reference in Inspector.
/// </summary>
public class BuoyDateTimePicker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DatabaseManager databaseManager;

    [Header("Display Settings")]
    [Tooltip("Tampilkan picker UI")]
    [SerializeField] private bool showPicker = true;

    [Tooltip("Posisi panel dari kiri layar")]
    [SerializeField] private float panelX = 15f;

    [Tooltip("Posisi panel dari atas layar")]
    [SerializeField] private float panelY = 15f;

    [Tooltip("Otomatis fetch data saat tanggal/jam berubah")]
    [SerializeField] private bool autoFetch = true;

    [Header("Initial Date (Read-Only)")]
    [SerializeField] private string currentDate = "";
    [SerializeField] private int currentHour = 7;

    // ─── Internal state ──────────────────────────────────────────
    private int displayYear;
    private int displayMonth;
    private int selectedDay;
    private int selectedHour;
    private bool calendarOpen = true;
    [SerializeField] private bool minimized = false;
    private float sliderValue;

    // ─── Layout constants ────────────────────────────────────────
    private const float PANEL_WIDTH = 310f;
    private const float CELL_SIZE = 38f;
    private const float HEADER_HEIGHT = 32f;
    private const float ROW_HEIGHT = 22f;
    private const float PADDING = 12f;
    private const float MINIMIZED_WIDTH = 200f;
    private const float MINIMIZED_HEIGHT = 32f;

    // ─── Colors ──────────────────────────────────────────────────
    private static readonly Color panelBg = new Color(0.04f, 0.04f, 0.08f, 0.92f);
    private static readonly Color borderColor = new Color(0.2f, 0.5f, 0.8f, 0.5f);
    private static readonly Color accentColor = new Color(0.25f, 0.55f, 0.9f, 1f);
    private static readonly Color accentDim = new Color(0.15f, 0.35f, 0.65f, 0.6f);
    private static readonly Color hoverColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color todayColor = new Color(0.3f, 0.7f, 0.4f, 0.35f);
    private static readonly Color textDim = new Color(0.45f, 0.45f, 0.5f);
    private static readonly Color textNormal = new Color(0.8f, 0.82f, 0.85f);
    private static readonly Color textBright = Color.white;
    private static readonly Color separatorColor = new Color(0.3f, 0.5f, 0.8f, 0.3f);
    private static readonly Color sliderBgColor = new Color(0.12f, 0.12f, 0.18f, 1f);
    private static readonly Color sliderFillColor = new Color(0.25f, 0.55f, 0.9f, 0.8f);
    private static readonly Color sliderHandleColor = new Color(0.4f, 0.7f, 1f, 1f);

    // ─── Styles (lazy init) ──────────────────────────────────────
    private GUIStyle titleStyle, monthLabelStyle, navBtnStyle, minimizeBtnStyle;
    private GUIStyle dayHeaderStyle, dayCellStyle, dayCellSelectedStyle, dayCellTodayStyle, dayCellDimStyle;
    private GUIStyle sectionLabelStyle, statusStyle, fetchBtnStyle, hourLabelStyle, tooltipStyle;
    private bool stylesInit;

    // ─── Day-of-week headers ─────────────────────────────────────
    private static readonly string[] dayHeaders = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

    // ═══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    private void Start()
    {
        if (databaseManager == null)
            databaseManager = FindObjectOfType<DatabaseManager>();

        // Initialize to today
        DateTime today = DateTime.Now;
        displayYear = today.Year;
        displayMonth = today.Month;
        selectedDay = today.Day;
        selectedHour = currentHour > 0 ? currentHour : today.Hour;
        sliderValue = selectedHour;

        UpdateCurrentDateString();
    }

    // ═══════════════════════════════════════════════════════════════
    //  ON GUI
    // ═══════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (!showPicker) return;
        InitStyles();

        if (minimized)
        {
            DrawMinimized();
            return;
        }

        // ─── Calculate panel height ──────────────────────────────
        float ph = PADDING; // top padding
        ph += HEADER_HEIGHT; // title bar
        ph += 4f;

        if (calendarOpen)
        {
            ph += ROW_HEIGHT; // month nav row
            ph += 6f;
            ph += ROW_HEIGHT; // day-of-week headers
            int weeks = GetWeeksInMonth(displayYear, displayMonth);
            ph += weeks * CELL_SIZE; // calendar grid
            ph += 8f;
        }
        else
        {
            // Compact date display
            ph += ROW_HEIGHT + 4f;
        }

        ph += 2f; // separator
        ph += ROW_HEIGHT + 4f; // "Jam" section label
        ph += 48f; // slider area (slider + labels)
        ph += 10f;
        ph += 28f; // fetch button
        ph += 6f; // status line
        ph += ROW_HEIGHT;
        ph += PADDING; // bottom padding

        float x = panelX;
        float y = panelY;

        // ─── Panel background ────────────────────────────────────
        Rect panelRect = new Rect(x, y, PANEL_WIDTH, ph);
        GUI.color = panelBg;
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        DrawBorder(panelRect, borderColor);

        float cx = x + PADDING;
        float cy = y + PADDING;
        float innerW = PANEL_WIDTH - PADDING * 2;

        // ─── Title bar ───────────────────────────────────────────
        GUI.Label(new Rect(cx, cy, innerW - 60f, HEADER_HEIGHT), "📅  DATE & TIME", titleStyle);

        // Toggle calendar button
        string toggleText = calendarOpen ? "▲" : "▼";
        if (GUI.Button(new Rect(x + PANEL_WIDTH - 68f, cy + 2f, 28f, 24f), toggleText, navBtnStyle))
            calendarOpen = !calendarOpen;

        // Minimize button
        if (GUI.Button(new Rect(x + PANEL_WIDTH - 36f, cy + 2f, 28f, 24f), "─", minimizeBtnStyle))
            minimized = true;

        cy += HEADER_HEIGHT + 4f;

        if (calendarOpen)
        {
            // ─── Month navigation ────────────────────────────────
            DrawMonthNavigation(ref cy, cx, innerW);
            cy += 6f;

            // ─── Calendar grid ───────────────────────────────────
            DrawCalendarGrid(ref cy, cx, innerW);
            cy += 8f;
        }
        else
        {
            // ─── Compact date display with nav ───────────────────
            DrawCompactDate(ref cy, cx, innerW);
            cy += 4f;
        }

        // ─── Separator ───────────────────────────────────────────
        GUI.color = separatorColor;
        GUI.DrawTexture(new Rect(cx, cy, innerW, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        cy += 2f;

        // ─── Hour slider ─────────────────────────────────────────
        GUI.Label(new Rect(cx, cy, innerW, ROW_HEIGHT), "JAM", sectionLabelStyle);
        cy += ROW_HEIGHT + 4f;
        DrawHourSlider(ref cy, cx, innerW);
        cy += 10f;

        // ─── Fetch button ────────────────────────────────────────
        Rect fetchRect = new Rect(cx, cy, innerW, 28f);
        if (GUI.Button(fetchRect, $"FETCH  {currentDate}  {selectedHour:D2}:00", fetchBtnStyle))
        {
            FetchSelectedData();
        }
        cy += 34f;

        // ─── Status ──────────────────────────────────────────────
        string statusText = databaseManager != null && databaseManager.IsLoading
            ? "⏳ Loading..."
            : (databaseManager != null && !string.IsNullOrEmpty(databaseManager.ActiveKey)
                ? $"✓ Active: {databaseManager.ActiveKey}"
                : "Ready");
        GUI.Label(new Rect(cx, cy, innerW, ROW_HEIGHT), statusText, statusStyle);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MINIMIZED VIEW
    // ═══════════════════════════════════════════════════════════════

    private void DrawMinimized()
    {
        float boxSize = 36f;
        float x = panelX;
        float y = panelY;

        Rect minRect = new Rect(x, y, boxSize, boxSize);
        GUI.color = panelBg;
        GUI.DrawTexture(minRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        DrawBorder(minRect, borderColor);

        // Styling tombol ikon emoji
        GUIStyle iconStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textBright },
            hover = { textColor = accentColor },
            active = { textColor = accentColor }
        };
        iconStyle.normal.background = null;
        iconStyle.hover.background = null;
        iconStyle.active.background = null;

        // Klik untuk mengembalikan ke tampilan penuh
        if (GUI.Button(minRect, "📅", iconStyle))
            minimized = false;

        // Menampilkan tooltip interaktif saat melayang di atas ikon
        if (minRect.Contains(Event.current.mousePosition))
        {
            string tooltipText = $"Pilih Waktu (📅 {currentDate} {selectedHour:D2}:00)";
            Vector2 tooltipSize = tooltipStyle.CalcSize(new GUIContent(tooltipText));
            tooltipSize.x += 12f;
            tooltipSize.y += 6f;

            float ttX = x + boxSize + 8f;
            float ttY = y + (boxSize - tooltipSize.y) / 2f;

            Rect ttRect = new Rect(ttX, ttY, tooltipSize.x, tooltipSize.y);
            GUI.color = panelBg;
            GUI.DrawTexture(ttRect, Texture2D.whiteTexture);
            
            DrawBorder(ttRect, borderColor);
            GUI.color = Color.white;

            GUI.Label(new Rect(ttX + 6f, ttY + 3f, tooltipSize.x, tooltipSize.y), tooltipText, tooltipStyle);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  COMPACT DATE DISPLAY
    // ═══════════════════════════════════════════════════════════════

    private void DrawCompactDate(ref float cy, float cx, float innerW)
    {
        float navBtnW = 28f;

        // ◀ Previous day
        if (GUI.Button(new Rect(cx, cy, navBtnW, ROW_HEIGHT), "◀", navBtnStyle))
        {
            selectedDay--;
            if (selectedDay < 1)
            {
                displayMonth--;
                if (displayMonth < 1) { displayMonth = 12; displayYear--; }
                selectedDay = DateTime.DaysInMonth(displayYear, displayMonth);
            }
            UpdateCurrentDateString();
            if (autoFetch) FetchSelectedData();
        }

        // Date label
        string dateStr = new DateTime(displayYear, displayMonth, selectedDay).ToString("dd MMMM yyyy");
        GUI.Label(new Rect(cx + navBtnW, cy, innerW - navBtnW * 2, ROW_HEIGHT), dateStr, monthLabelStyle);

        // ▶ Next day
        if (GUI.Button(new Rect(cx + innerW - navBtnW, cy, navBtnW, ROW_HEIGHT), "▶", navBtnStyle))
        {
            selectedDay++;
            int daysInMonth = DateTime.DaysInMonth(displayYear, displayMonth);
            if (selectedDay > daysInMonth)
            {
                selectedDay = 1;
                displayMonth++;
                if (displayMonth > 12) { displayMonth = 1; displayYear++; }
            }
            UpdateCurrentDateString();
            if (autoFetch) FetchSelectedData();
        }

        cy += ROW_HEIGHT;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MONTH NAVIGATION
    // ═══════════════════════════════════════════════════════════════

    private void DrawMonthNavigation(ref float cy, float cx, float innerW)
    {
        float navBtnW = 32f;
        float navH = ROW_HEIGHT;

        // ◀ Previous month
        if (GUI.Button(new Rect(cx, cy, navBtnW, navH), "◀", navBtnStyle))
        {
            displayMonth--;
            if (displayMonth < 1) { displayMonth = 12; displayYear--; }
            ClampSelectedDay();
        }

        // Month/Year label
        string monthName = new DateTime(displayYear, displayMonth, 1).ToString("MMMM yyyy");
        GUI.Label(new Rect(cx + navBtnW, cy, innerW - navBtnW * 2, navH), monthName, monthLabelStyle);

        // ▶ Next month
        if (GUI.Button(new Rect(cx + innerW - navBtnW, cy, navBtnW, navH), "▶", navBtnStyle))
        {
            displayMonth++;
            if (displayMonth > 12) { displayMonth = 1; displayYear++; }
            ClampSelectedDay();
        }

        cy += navH;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CALENDAR GRID
    // ═══════════════════════════════════════════════════════════════

    private void DrawCalendarGrid(ref float cy, float cx, float innerW)
    {
        float cellW = innerW / 7f;

        // Day-of-week headers
        for (int i = 0; i < 7; i++)
        {
            Rect headerRect = new Rect(cx + i * cellW, cy, cellW, ROW_HEIGHT);
            GUI.Label(headerRect, dayHeaders[i], dayHeaderStyle);
        }
        cy += ROW_HEIGHT;

        // Calendar cells
        int daysInMonth = DateTime.DaysInMonth(displayYear, displayMonth);
        // Monday = 0, Sunday = 6
        int firstDayOfWeek = ((int)new DateTime(displayYear, displayMonth, 1).DayOfWeek + 6) % 7;

        DateTime today = DateTime.Now;
        bool isCurrentMonth = (displayYear == today.Year && displayMonth == today.Month);

        int dayNum = 1;
        int weeks = GetWeeksInMonth(displayYear, displayMonth);

        for (int w = 0; w < weeks; w++)
        {
            for (int d = 0; d < 7; d++)
            {
                int cellIndex = w * 7 + d;
                Rect cellRect = new Rect(cx + d * cellW, cy + w * CELL_SIZE, cellW, CELL_SIZE);

                if (cellIndex < firstDayOfWeek || dayNum > daysInMonth)
                {
                    // Empty cell — previous/next month days
                    continue;
                }

                int thisDay = dayNum;
                bool isSelected = (thisDay == selectedDay);
                bool isToday = (isCurrentMonth && thisDay == today.Day);
                bool isWeekend = (d >= 5); // Saturday, Sunday

                // Cell background
                if (isSelected)
                {
                    GUI.color = accentColor;
                    DrawRoundedRect(cellRect, 4f);
                    GUI.color = Color.white;
                }
                else if (isToday)
                {
                    GUI.color = todayColor;
                    DrawRoundedRect(cellRect, 4f);
                    GUI.color = Color.white;
                }
                else if (cellRect.Contains(Event.current.mousePosition))
                {
                    GUI.color = hoverColor;
                    GUI.DrawTexture(cellRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                // Day number
                GUIStyle cellStyle = isSelected ? dayCellSelectedStyle
                    : (isWeekend ? dayCellDimStyle : dayCellStyle);
                GUI.Label(cellRect, thisDay.ToString(), cellStyle);

                // Click handler
                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                {
                    selectedDay = thisDay;
                    UpdateCurrentDateString();
                    if (autoFetch) FetchSelectedData();
                }

                dayNum++;
            }
        }

        cy += weeks * CELL_SIZE;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HOUR SLIDER
    // ═══════════════════════════════════════════════════════════════

    private void DrawHourSlider(ref float cy, float cx, float innerW)
    {
        // Current hour display
        GUI.Label(new Rect(cx, cy, innerW, ROW_HEIGHT),
            $"{selectedHour:D2}:00", hourLabelStyle);
        cy += ROW_HEIGHT + 2f;

        // Custom slider track
        float sliderH = 8f;
        float handleW = 14f;
        float handleH = 20f;
        float trackY = cy + (handleH - sliderH) / 2f;

        // Track background
        Rect trackRect = new Rect(cx, trackY, innerW, sliderH);
        GUI.color = sliderBgColor;
        GUI.DrawTexture(trackRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Track fill (up to current position)
        float fillW = (sliderValue / 23f) * innerW;
        GUI.color = sliderFillColor;
        GUI.DrawTexture(new Rect(cx, trackY, fillW, sliderH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Tick marks for every 6 hours
        for (int t = 0; t <= 24; t += 6)
        {
            float tickX = cx + (t / 23f) * (innerW - 1);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(tickX, trackY - 2f, 1f, sliderH + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Handle
        float handleX = cx + (sliderValue / 23f) * (innerW - handleW);
        Rect handleRect = new Rect(handleX, cy, handleW, handleH);
        GUI.color = sliderHandleColor;
        GUI.DrawTexture(handleRect, Texture2D.whiteTexture);
        // Handle border
        GUI.color = new Color(0.2f, 0.4f, 0.7f, 0.8f);
        GUI.DrawTexture(new Rect(handleRect.x, handleRect.y, handleRect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(handleRect.x, handleRect.yMax - 1, handleRect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(handleRect.x, handleRect.y, 1, handleRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(handleRect.xMax - 1, handleRect.y, 1, handleRect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Invisible slider on top for interaction
        float newSlider = GUI.HorizontalSlider(
            new Rect(cx, cy + 2f, innerW, handleH),
            sliderValue, 0f, 23f);

        // Snap to integer hours
        int newHour = Mathf.RoundToInt(newSlider);
        if (newHour != selectedHour)
        {
            selectedHour = newHour;
            sliderValue = newHour;
            currentHour = newHour;
            UpdateCurrentDateString();
            if (autoFetch) FetchSelectedData();
        }
        sliderValue = newSlider;

        cy += handleH + 2f;

        // Hour tick labels
        for (int t = 0; t <= 24; t += 6)
        {
            int displayT = t == 24 ? 23 : t;
            float labelX = cx + (displayT / 23f) * innerW - 12f;
            GUI.Label(new Rect(labelX, cy, 28f, 14f), $"{displayT:D2}", statusStyle);
        }
        cy += 14f;
    }

    // ═══════════════════════════════════════════════════════════════
    //  DATA FETCH
    // ═══════════════════════════════════════════════════════════════

    private void FetchSelectedData()
    {
        if (databaseManager == null)
        {
            Debug.LogWarning("[BuoyDateTimePicker] DatabaseManager not assigned!");
            return;
        }

        string key = $"{displayYear:D4}{displayMonth:D2}{selectedDay:D2}{selectedHour:D2}";
        Debug.Log($"[BuoyDateTimePicker] Fetching key: {key}");
        databaseManager.ActiveKey = key;
        databaseManager.FetchDataByKey(key);
    }

    private void UpdateCurrentDateString()
    {
        currentDate = $"{displayYear:D4}-{displayMonth:D2}-{selectedDay:D2}";
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private int GetWeeksInMonth(int year, int month)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        int firstDayOfWeek = ((int)new DateTime(year, month, 1).DayOfWeek + 6) % 7;
        return Mathf.CeilToInt((firstDayOfWeek + daysInMonth) / 7f);
    }

    private void ClampSelectedDay()
    {
        int daysInMonth = DateTime.DaysInMonth(displayYear, displayMonth);
        if (selectedDay > daysInMonth)
            selectedDay = daysInMonth;
        UpdateCurrentDateString();
    }

    private void DrawBorder(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawRoundedRect(Rect rect, float pad)
    {
        // Simulated rounded rect with slightly inset fill
        Rect inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        GUI.DrawTexture(inner, Texture2D.whiteTexture);
    }

    // ═══════════════════════════════════════════════════════════════
    //  STYLES
    // ═══════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = textBright }
        };

        monthLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textBright }
        };

        navBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = accentColor },
            hover = { textColor = textBright },
            active = { textColor = textBright },
        };
        // Transparent button background
        navBtnStyle.normal.background = null;
        navBtnStyle.hover.background = null;
        navBtnStyle.active.background = null;

        minimizeBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textDim },
            hover = { textColor = textBright },
            active = { textColor = textBright },
        };
        minimizeBtnStyle.normal.background = null;
        minimizeBtnStyle.hover.background = null;
        minimizeBtnStyle.active.background = null;

        dayHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = accentDim }
        };

        dayCellStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textNormal }
        };

        dayCellSelectedStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textBright }
        };

        dayCellTodayStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.4f, 0.9f, 0.5f) }
        };

        dayCellDimStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textDim }
        };

        sectionLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = accentColor }
        };

        hourLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textBright }
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
        };

        fetchBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textBright },
            hover = { textColor = textBright },
            active = { textColor = textBright },
        };

        tooltipStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = textBright }
        };
    }
}
