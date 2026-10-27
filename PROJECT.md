# DynamicOmrFiller — Project Documentation

> Reference document for any AI or developer joining this project. Read this first to understand what's been built, how the pieces fit together, and how to extend it.

---

## 1. Overview

**DynamicOmrFiller** is a Windows desktop tool that automatically fills blank scanned **OMR (Optical Mark Recognition) answer sheets** with synthetic data. It reads a real scanned image, detects the bubble grid using fiducial markers, and draws filled black dots on top of the correct bubbles for:

- **Roll numbers** (mapped per digit)
- **Registration numbers** (mapped per digit)
- **MCQ answers** (random choice per question)
- **SAQ labels** (rendered text in designated cells)
- **Question Quality** bubbles (random pick from S/H/E or any comma-separated set defined in XML)

It also supports **multi-page sheets** (one student spans multiple pages) by reading a `Page ID` bubble on each scan and cycling through the supplied roll/registration list only when page 1 is detected.

**Primary use case:** generating realistic test data to QA an OMR scoring pipeline — without manually filling hundreds of physical sheets.

---

## 2. Tech Stack

| Layer | Choice |
|---|---|
| Language | C# (`AllowUnsafeBlocks=true` for fast pixel access) |
| Runtime | .NET Framework 4.7.2 |
| UI | Windows Forms (WinForms), custom-painted flat theme (`UiTheme.cs`) |
| Image processing | `System.Drawing` + `BitmapData.LockBits` (no OpenCV) |
| XML config parsing | `System.Xml.Linq` |
| Excel/CSV parsing | Built-in `System.IO.Compression.ZipArchive` + `XDocument` (no NuGet) |
| Build | MSBuild 17.x (Visual Studio 2022) |

No external NuGet packages. Everything ships with .NET Framework 4.7.2.

---

## 3. File Structure

```
DynamicOmrFiller/
├── AppEntry.cs            STAThread Main entry point
├── AppSettings.cs         User defaults persisted to %APPDATA%\DynamicOmrFiller\settings.xml
├── ColumnMapDialog.cs     Pick which spreadsheet column is roll / registration
├── PdfRenderer.cs         PdfPageSource - renders PDF pages via the Windows PDF engine
├── MainForm.cs            WinForms UI shell (header / cards / fixed action bar)
├── UiTheme.cs             Palette + custom controls (CardPanel, InputHost,
│                          RoundedButton, ToggleSwitch, SegmentedToggle)
├── OmrModels.cs           DTOs: GridSystemData, OmrRegion, OmrBatchOptions,
│                          OmrBatchResult, OmrLogEventArgs
├── OmrProcessor.cs        Core image processing engine (~750 lines)
├── ExcelLoader.cs         Static .xlsx + .csv parser, no external deps
├── OutputResultDialog.cs  Custom result dialog with copyable output path
├── Properties/
│   └── AssemblyInfo.cs    AssemblyVersion (read at runtime, shown in UI)
├── DynamicOmrFiller.csproj
├── DynamicOmrFiller.sln
├── App.config             .NET Framework runtime config
├── app.manifest           System DPI awareness + supported-OS declarations
├── app.ico                Application icon (loaded at runtime)
├── PROJECT.md             ← this file
└── Program.cs             LEGACY console prototype, NOT in <Compile Include>,
                           safe to delete
```

---

## 4. Architecture & Data Flow

```
┌────────────┐    OmrBatchOptions   ┌─────────────────┐   per-image   ┌─────────────┐
│ MainForm   │────────────────────▶│ OmrProcessor    │──────────────▶│ Bitmap save │
│ (UI)       │                     │ ProcessBatch()  │               └─────────────┘
└────┬───────┘                     └────────┬────────┘
     │ logs (LogMessage event)              │
     ▼                                      ▼
┌─────────────┐                    For each image:
│ StatusStrip │                    1. DetectAndCalculateGrid
└─────────────┘                    2. DetectPageIdFromImage
     │                             3. Cycle Roll/Reg if page == "1"
     │                             4. LoadRegionsForPage(pageId)
     ▼                             5. For each region, dispatch by MemberName
┌──────────────────┐
│ OutputResultDialog│ ◀── shown after batch completes
└──────────────────┘
```

### Region dispatch (in `OmrProcessor.ProcessOmrImage`)

| `MemberName` matches | Method | Behavior |
|---|---|---|
| `rollNo` (exact) | `DrawMappedField` + `DrawWrittenNumber` | Maps each digit of `currentRoll` to a row index in `CharSet` and draws a dot; also prints the digit in the write-in box above that column when *Write numbers* is on |
| `registrationNo` (exact) | `DrawMappedField` + `DrawWrittenNumber` | Same as above for `currentReg` |
| `answer*` (StartsWith) | `DrawAnswers` | Uses the user's MCQ answer key when one is supplied, otherwise a random char from `CharSet` per row |
| `SaqAnswer*` (StartsWith) | `DrawSaqMappingLabel` | Renders user-supplied SAQ text + member-name label inside the cell |
| `QuestionQuality` (exact, **only if checkbox is on**) | `DrawRandomQuestionQuality` | Splits CharSet on `,` (e.g. `"S,H,E"`), picks one at random |
| anything else | (silently skipped) | |

---

## 5. OMR Processing Pipeline (detailed)

### Step 1 — Grid Detection (`DetectAndCalculateGrid`)

Goal: find three fiducial markers (top-left, top-right, bottom-left) and the timing-mark column on the left edge.

**Half-page sheets.** The bottom-left marker is searched over the whole column
below the top marker, not a fixed bottom quarter, and `FindMarkerRect` takes the
top marker's size as `expectedSize`: components outside 30% of it, or less than
60% solid, are rejected outright. Without that, a sheet whose OMR block occupies
only the top of the page (question paper underneath) locks onto a letter from
the question text - measured on `Engineering 20+1 A4`, a full A4 page produced a
16x12 grid at 142 px instead of 57x31 at 39 px.

1. **`FindMarkerRect`** runs a flood-fill over a corner search region (e.g. `0..width/5 × 0..height/4` for top-left). For each connected dark component (pixel value `< 80`), it scores by:
   - **Distance** from the expected corner pixel (target-distance penalty)
   - **Aspect ratio** closeness to 1.0
   - **Fill ratio** closeness to 1.0 (solid square)
   - **Size penalty** — preference for ~36px markers
   The lowest-scoring component wins.
2. **`FindTimingMarks`** scans a vertical column at `(leftTop.X + leftBottom.X)/2 + leftTop.Width/2` between the top and bottom markers. Each contiguous dark run > 3px is a timing mark.
3. **`StepY`** = average gap between timing marks; **`StepX`** = `StepY` (square-grid assumption).
4. `XLines` = all X-positions from `leftTop.Right` to `rightTop.Left` in `StepX` increments.
5. `YLines` = `[leftTop.Bottom, ...timing-mark-tops..., leftBottom.Top]`.

These hard-coded constants (80, 36, 3) are tuned for ~300 DPI scans. Different DPI may need re-tuning.

### Step 2 — Page ID Detection (`DetectPageIdFromImage`)

Reads the `<omrInfo memberName="omrNo">` element from the XML and resolves the
page id, which then selects the `<omr omrNo="…">` section to draw.

| omrInfo | Result |
|---|---|
| `isVirtual="true" defaultValue="2"` | **`"2"`** — no bubbles on the sheet, the id is stated in the XML |
| no `charSet` | `defaultValue`, else `"1"` |
| single-token `charSet` (e.g. `"1"`) | that token |
| multi-token `charSet` (`"1,2,3"` / `"123"`) | samples darkness at each candidate mark with `GetDarknessPercentage`, picks the darkest above `BlackPixelPercent`; with no `defaultValue` an unreadable mark returns null and the page is rejected rather than guessed |

**Mark geometry.** A page-id mark is not always a whole grid cell:
`resultCountCellX` / `resultCountCellY` give its real size, so the pitch between
candidates is `(cell + spacing) x step` and the sample point is that cell's own
centre. The Engineering sheet uses `resultCountCellY=".5" spacing y=".5"`, i.e.
marks exactly one row apart; assuming a full cell put the sample 1.5 rows apart,
landing in the gap between the two marks and reading neither.

**A page id with no matching `<omr>` section now throws** ("The XML has no
`<omr omrNo="1">` section. It defines: 2.") instead of silently drawing
nothing. `ProcessOmrImage` also throws when a section matches but no region
inside it could be filled, and logs `Regions filled: N of M` either way.

### Step 3 — Roll/Reg Cycling

`studentIndex` advances when a sheet **starts a new student**, decided by
`FirstPageId(omrInfo)` rather than the literal `"1"`:

| Design | `FirstPageId` | New student on |
|---|---|---|
| Virtual omrInfo, or no `charSet`, or a single-token `charSet` | `null` | **every sheet** (one page per student) |
| Multi-token `charSet` (`"1,2,3"`) | first token (`"1"`) | sheets detected as that page |

A 2-page sheet for student N therefore keeps the same roll on both pages, while
a single-page design gives every sheet its own roll. If `studentIndex` exceeds
the supplied roll list, the last roll is reused (logged).

### Step 4 — Region Rendering

All draw methods compute pixel positions from grid:
```
originX = grid.XLines[region.StartX - 1]   // 1-based to 0-based
originY = grid.YLines[region.StartY - 1]
pixelX  = originX + (region.PaddingX * grid.StepX) + (offset * grid.StepX * (1 + region.SpacingX))
pixelY  = originY + (region.PaddingY * grid.StepY) + (offset * grid.StepY * (1 + region.SpacingY))
```
Then `DrawDot` fills an ellipse at (pixelX + StepX/2, pixelY + StepY/2) with diameter = `_dotSizePixels` (user setting) or `min(StepX, StepY) * 0.6` if 0.

**SAQ cell layout** (`DrawSaqMappingLabel` → `DrawSaqCell`): the auto-generated
label (the region's `memberName`) keeps a strip along the bottom of the cell,
and the user's text is drawn in the space above it, aligned by
`SaqAlignHorizontal` / `SaqAlignVertical`. Because the two never share a
rectangle, "move the text to the bottom" cannot collide with the label.

The label can be switched off with *Cell label* (`OmrBatchOptions.WriteSaqLabel`,
default on). With it off the strip is not reserved at all, so the SAQ text gets
the whole cell rather than leaving a gap where the label used to be, and
*Label size (px)* greys out since it no longer means anything.

| Setting | 0 / default | Otherwise |
|---|---|---|
| `SaqFontSizePixels` | `max(14, min(26, StepY * 0.6))` ≈ 22 px at 300 dpi | that pixel size |
| `SaqLabelFontSizePixels` | `max(10, min(18, StepY * 0.4))` ≈ 14 px | that pixel size |
| `SaqAlignHorizontal` / `SaqAlignVertical` | `Center` | `Near` = left/top, `Far` = right/bottom |

### Step 4a — Written roll / registration digits (`DrawWrittenNumber`)

These sheets print a row of empty boxes above each bubble grid, where a
candidate writes the digits by hand. With the *Write numbers* toggle on
(`OmrBatchOptions.WriteStudentNumbers`, default **true**) the filler prints them
too, so the output looks like a real completed sheet rather than bubbles alone.

- **Columns** use the same X formula as the dots
  (`originX + PaddingX*StepX + i*StepX*(1+SpacingX)`, width `StepX`), so every
  digit sits directly over its own bubble column.
- **Vertical position** is the *Write offset* control
  (`OmrBatchOptions.WriteOffsetRows`, default **1.0**): the digits are centred
  that many grid rows above the first bubble row. `1.0` lands in the middle of
  the printed box row on the College Admission sheet; raise it to move the
  digits up, lower it to move them down. The box itself stays
  `WriteBandCells = 2` rows tall, which is what the font size is derived from.
  Because the unit is grid rows, the same value holds at any scan resolution.
  An offset that pushes the digits off the top of the sheet is skipped and
  logged rather than throwing.
- **Font size** is `min(bandHeight * 0.62, StepX * 1.15)`, so a digit stays
  inside its box on both axes whatever the scan resolution.
- A value longer than the region's `lines` is truncated to the available
  columns and logged, rather than spilling past the grid.

Only `rollNo` and `registrationNo` are written; nothing else uses this.

### Step 5 — Output

- Filename: `{originalName}_edit_{HHmmss_fff}{ext}` (timestamp avoids collisions in batch).
- Format: JPEG / PNG / BMP based on input extension.
- **Output folder resolution** (in `ResolveOutputFolder`): if `options.OutputFolder` is empty/whitespace, auto-creates `FillUpOMR/` inside the input file's parent directory (or inside the input folder for batch mode). The resolved path is **written back to `options.OutputFolder`** so MainForm can display and open it after the batch.

---

## 6. XML Configuration Format

Expected by `LoadOmrInfoRegion` and `LoadRegionsForPage`. Outline:

```xml
<Page name="UMS">
  <design designFor="..." mappingStyle="..." gridX="36" gridY="109" blackPixelPercent="60">

    <!-- Page-ID detector -->
    <omrInfo memberName="omrNo" charSet="1,2,3" direction="y" lines="3" ...>
      <startCircle x="..." y="..." />
      <startPadding x="..." y="..." />
    </omrInfo>

    <!-- Page 1 -->
    <omr omrNo="1">
      <region direction="y" charSet="0123456789" lines="11" memberName="rollNo">
        <startCircle x="4" y="19" />
        <startPadding x="0.5" y="0.5" />
        <spacing x="0.5" y="0.5" />
      </region>

      <region direction="x" charSet="ABCD" lines="20" memberName="answer">
        <startCircle x="5" y="42" />
        <startPadding x="0" y="0" />
        <spacing x="1" y="0.5" />
      </region>

      <region direction="x" charSet="S,H,E" lines="1" memberName="QuestionQuality">
        <startCircle x="19" y="108" />
        <spacing x="4.5" y="1" />
      </region>
      ...
    </omr>

    <omr omrNo="2"> ... </omr>
  </design>
</Page>
```

### Region attributes parsed (`OmrProcessor.ParseRegion`)

| Attribute | Stored as | Notes |
|---|---|---|
| `memberName` | `string` | Dispatch key |
| `direction` | `string` (`"x"` or `"y"`) | Default `"y"` |
| `charSet` | `string` | Either char-per-bubble (`"ABCD"`) or comma-separated tokens (`"S,H,E"`) |
| `lines` | `int` (default 1) | Question rows |
| `<startCircle x y />` | `StartX`, `StartY` ints (1-based) | |
| `<startPadding x y />` | `PaddingX`, `PaddingY` floats | In cell-step units |
| `<spacing x y />` | `SpacingX`, `SpacingY` floats | Extra gap as multiplier |
| `resultCountCellX/Y` | floats | SAQ rendering dimensions |
| `blackPixelPercent` | double (default 20) | Used by `DetectPageIdFromImage` |
| `isVirtual` | bool (on `omrInfo`) | `true` → the page id is taken from `defaultValue`, no bubbles are read |
| `defaultValue` | string (on `omrInfo`) | The page id to use when virtual, or when detection finds nothing |
| `lineStart` | int (default 0 = unset) | The real question number of an `answer*` region's first row |

Other XML attributes (`useSeparator`, `useErrorChar`, `joinOutput`, `lineStart`, `borderRemovePercent`, etc.) are **ignored** by this filler — they are read by the OMR scoring side, not the synthetic filler.

---

## 7. UI Walkthrough (MainForm + UiTheme)

Since v1.3.0 the shell is a **card-based flat design** rendered by custom-painted
controls in `UiTheme.cs`. No `GroupBox`, no `StatusStrip` — everything is drawn
from one palette so the look can be retuned in a single file.

### Window frame (`MainForm.BuildLayout`)
A root `TableLayoutPanel` with three rows:

The window height **auto-fits the cards**: `MainForm.FitToContent()` runs on
`OnLoad` and again whenever a card raises `CardPanel.CollapsedChanged`, so
collapsing a card shrinks the window instead of leaving dead space, and
expanding one grows it — clamped to the screen working area (past that the body
scrolls) and to `MinimumSize`. Width is never touched.

| Row | Height | Content |
|---|---|---|
| 0 | 84 px | **Header** — `IconTile` (app.ico in a rounded accent tile), title + subtitle, `PillLabel` version chip on the right, 1px bottom rule |
| 1 | fills | **Body** — `AutoScroll` panel holding a vertical stack of `CardPanel`s |
| 2 | 74 px | **Footer** — `SlimProgress` strip, `StatusDot` + status text, primary **Process OMR** button |

The footer is **fixed**: the action button and live status stay visible while the
body scrolls.

### Cards (`CardPanel`)
Each card is a rounded white surface (radius 10, 1px `Theme.Border`) with a
glyph, a title and a one-line subtitle, then a rule, then a field grid built by
`CardPanel.MakeGrid(columns)`. Rows are appended with `MainForm.AddRow(grid)`.

**Collapse / expand:** every card header is a toggle. Clicking anywhere on the
header (icon, title, subtitle or the chevron on the right) flips
`CardPanel.Collapsed`, which hides the card's `_content` table; the card
auto-sizes down to just the header row. The chevron swaps between
`Theme.GlyphChevronUp` and `Theme.GlyphChevronDown`.

Set `card.Collapsed = true` **after** `card.Add(grid)` so the inner controls
lay out once while still visible. Only **Source files** starts open; cards 2-5
below all start collapsed, so the form opens compact and nothing scrolls.

1. **Source files** — mode `SegmentedToggle` (*Single image* / *Folder batch* /
   *PDF file*),
   input path, output path + hint, XML config. All three path fields accept
   **drag-drop** (on both the `TextBox` and its `InputHost` shell).
2. **Student information** — Roll numbers | Registration side by side as
   **multiline text areas** (one value per line, or comma-separated), a live
   count line beneath them, a soft **Load from Excel / CSV** button (see §8),
   a format hint, a *Write numbers* `ToggleSwitch` (**default on**) with its
   *Write offset* spin box (grid rows, default **1.0**, disabled while the
   toggle is off — see §5 Step 4a) and a **Save as default** button (see §7b).

   `UpdateStudentCountHint` counts both boxes with `OmrProcessor.SplitValues`
   — the same splitter the processor uses, so the count can never drift from
   what actually gets filled. The two lists are paired by position, so unequal
   non-zero counts turn the line amber (`Theme.Warning`) and say the shorter
   list runs out first. An empty registration list is legitimate and stays
   neutral.
3. **MCQ answer key** — a multiline box for the per-question MCQ answers, an
   *Other questions* `SegmentedToggle` (**Fill randomly** / **Leave blank**) and
   a live hint summarising what the key currently covers (see §7a).
4. **SAQ content** — *Text size*, a *Cell label* `ToggleSwitch` (**default on**)
   with its *Label size* (both sizes 0–200 px, `0` = auto from the grid, the
   size greyed out while the label is off), a *Text position* pair of
   `SegmentedToggle`s (Left/Centre/Right and Top/Middle/Bottom), the multiline
   SAQ text box, and a **Save as default** button (see §7b).
5. **Settings** — dot size (1–200) + recommendation hint, and three
   `ToggleSwitch`es: *Fill Question Quality randomly* (**default on**),
   *Export the filled answers as an answer_key CSV* (default off) and
   *Open the output folder when processing finishes* (default off).

### Custom controls in `UiTheme.cs`

| Control | Purpose |
|---|---|
| `Theme` | Colour / font / MDL2 glyph palette + `RoundedRect` + `ParentBack` helpers |
| `CardPanel` | Rounded white card with icon, title, subtitle, divider and a row stack; the header collapses / expands the body |
| `InputHost` | Rounded, focus-aware shell around a borderless `TextBox` / `NumericUpDown`; border turns accent blue on focus |
| `RoundedButton` | Antialiased flat button with hover/press/disabled states, optional glyph; `Primary` / `Secondary` / `Soft` factories |
| `SegmentedToggle` | Pill segment selector (drives the single-file vs folder mode) |
| `ToggleSwitch` | iOS-style pill switch; still a real `CheckBox`, so `.Checked` semantics are unchanged |
| `SlimProgress` | 3px indeterminate strip animated by a 16 ms `Timer` |
| `StatusDot` | Colour-coded dot: green ready/done, blue busy, amber partial failure, red error |
| `PillLabel` | Rounded version badge |
| `IconTile` | Rounded accent tile that draws the app icon (falls back to a glyph) |
| `EdgePanel` | Panel that can draw a 1px rule on its top or bottom edge |

**Transparency contract:** every control that sets `BackColor = Color.Transparent`
must also enable `ControlStyles.SupportsTransparentBackColor` in `SetStyle`, or
the constructor throws *"Control does not support transparent background colors"*.
`Theme.ParentBack` then walks up to the first opaque parent to clear the corner
pixels — which is why containers **inside** a card are set to `Theme.Surface`
rather than left transparent.

### Input mode
`_modeToggle.SelectedIndex == 0` (exposed as `MainForm.IsSingleFileMode`) is the
single source of truth; it replaced the old `_isSingleFile` CheckBox and feeds
`CollectOptions`, `BrowseInputPath`, `UpdateInputModeUi` and `ResolveInputDrop`.

### High-DPI
`app.manifest` declares `<dpiAware>true</dpiAware>` (system DPI awareness) and
the form uses `AutoScaleMode.Font`, so the shell renders sharp on scaled displays
instead of being bitmap-stretched by Windows.

### Drag and drop helper (`EnableFileDrop`)
`EnableFileDrop(Control target, TextBox box, Func<string[], string> resolver)`
wires `AllowDrop` + `DragEnter` + `DragDrop`. The resolver returns the resolved
path or `null` to reject; the cursor reflects accept (Copy) vs reject (None)
before the drop happens. It is called twice per field — once for the `TextBox`
and once for its `InputHost` — so the padding around the text also accepts drops.

Validators in MainForm:
- `ResolveInputDrop`: image file when single mode; folder otherwise
- `ResolveOutputDrop`: folder only
- `ResolveXmlDrop`: file with `.xml` extension

---

## 7c. PDF input (`PdfRenderer.cs`)

Selecting **PDF file** renders every page of the chosen PDF at 300 dpi
(`PdfRenderWidth = 2480` px across A4, the resolution marker detection is tuned
for) and feeds each page through the normal pipeline.

**No SDK, no NuGet, no native DLL.** `PdfPageSource` reaches
`Windows.Data.Pdf` entirely by late binding: the WinRT types resolve from the
OS metadata in `System32\WinMetadata`, but the objects that come back are bare
`__ComObject` with no projected interfaces, so every call goes through the
*declared* interface type and an async operation's `Status` is read off
`Windows.Foundation.IAsyncInfo` rather than off the returned object.
`PdfPageSource.IsAvailable` reports whether the engine is reachable at all.

**Pages that are not this sheet are skipped, not failed.** A PDF normally
carries the question paper alongside the answer sheets, so a page that fails
grid detection, fails the `gridX`/`gridY` check, or whose page-number bubble
cannot be read is counted in `OmrBatchResult.SkippedCount` and logged. In image
modes the same conditions are still failures - there the user pointed at that
one file deliberately.

**The rendered size is normalised.** Windows multiplies `DestinationWidth` by
the display''s scale factor, so a 125% monitor hands back a 3100px page when
2480 was asked for. `RenderPage` rescales anything that is not the requested
width, otherwise the marker detection - which is tuned to a pixel scale - would
behave differently on each machine.

**Output is cropped to the sheet.** A PDF page is the whole A4 sheet, so on a
half-page design the question paper would otherwise be kept too.
`CropToSheet` trims to `GridSystemData.SheetBounds` - the bounding box of the
three fiducial markers - plus two grid rows of margin. Measured against the
pre-cropped sheets this lands within ~20 px of how they are trimmed by hand
(2417x1403 against 2429x1381). Controlled by *Crop PDF pages to the OMR sheet*
in Settings (`OmrBatchOptions.CropPdfToSheet`, default on); **image and folder
modes are never cropped**, since those inputs are already the sheet.

Output is named `{pdf}_p{n}_edit_{HHmmss_fff}.jpg`; pages are rendered and
disposed one at a time (an A4 page at 300 dpi is ~26 MB as a bitmap).

---

## 7b. Saved Defaults (`AppSettings.cs`)

`%APPDATA%\DynamicOmrFiller\settings.xml`, written only when the user presses a
**Save as default** button. Two independent groups:

| Group | Saved | Restored by |
|---|---|---|
| `<student>` | Roll numbers, Registration numbers, *Write numbers*, *Write offset* | `SeedDefaults` |
| `<saq>` | SAQ text, text size, *Cell label* on/off, label size, horizontal + vertical alignment | `SeedDefaults` |

Each group carries a `Has…Defaults` flag (set when its element exists in the
file), so an install that has never pressed Save still starts with the built-in
samples. `AppSettings.Load` swallows any exception and returns plain defaults —
a damaged settings file must never block startup. Elements added in later
versions (`writeNumbers` in 1.8.0, `writeOffsetRows` in 1.9.0) are read with a
fallback, so a file written by an older build still loads. `writeOffsetRows` is
a float, so it is read and written with `CultureInfo.InvariantCulture` — a
comma-decimal locale must not corrupt the file.

**Line endings:** XML collapses CRLF to LF on read, which a multiline `TextBox`
renders as one run-on line, so `NormalizeNewLines` puts `Environment.NewLine`
back on every string loaded from the file.

---

## 7a. MCQ Answer Key + Export

### Supplying a key
The **MCQ answer key** box drives `OmrBatchOptions.AnswerKeyInput`. It affects
**only** the `answer*` regions — roll number, registration number, SAQ and
Question Quality regions ignore it entirely.

`OmrProcessor.ParseAnswerKey` (public, also used by the UI for the live hint)
returns a `Dictionary<int, string[]>`: question number to the options to bubble.
An entry with an **empty array** is a question the user deliberately blanked;
a question **absent from the map** is not covered by the key at all.

| Input | Meaning |
|---|---|
| `1-A,2-AC,3-B,4-ABCD` | Q1=A, Q2=**A and C both**, Q3=B, Q4=**all four** |
| `1-A,2-AC,3-B,,,,` | Q1–Q3 answered, then Q4–Q7 blank (one blank per empty slot) |
| `4-` | Q4 blank, stated explicitly |
| `1-A,5-D` | only Q1 and Q5 are covered; Q2–Q4 are not |
| `A,C,B,D` | positional — Q1=A, Q2=C, Q3=B, Q4=D |
| `ACBD` | shorthand (no separator anywhere) — one answer per question |

Rules:
- Tokens are split on `,` `;` and newline. **Empty slots are significant** —
  each one blanks the next question, so `3-B,,,` leaves three questions blank.
- `N-answers` sets the question number explicitly and moves the cursor there;
  a token without `N-` takes the next position.
- Several letters after the dash bubble several options on that one question
  (`2-AC`). They may also be separated: `2-A/C`, `2-A C`, `2-A+C`.
- Matching against the region's `charSet` is **case-insensitive**.

### Questions the key does not list
The **Other questions** `SegmentedToggle` under the key box decides what happens
to every question that is not in the map (`OmrBatchOptions.UnlistedAnswersRandom`):

| Setting | Effect |
|---|---|
| **Fill randomly** (default) | Each uncovered question gets a random option — with an empty key this is the original behaviour: everything random |
| **Leave blank** | Uncovered questions get no dot at all — with an empty key this produces sheets with roll/registration filled but no MCQ answers |

Note this is separate from the explicit blanks above: `,,` always blanks, in
both modes.

### Question numbering
A region with `lineStart="N"` numbers its rows `N, N+1, …` — the College
Admission sheet has two `answer` regions, `lineStart="1"` and `lineStart="11"`,
so the key''s `11-B` really does land on question 11 regardless of XML order.

Without `lineStart`, `_questionCursor` supplies a running number. It is **reset
whenever a sheet starts a new student** (see §5 Step 3), so on a multi-page
sheet the key continues across that student''s pages (page 1 → Q1…, page 2 →
Q21…) and restarts for the next student. Regions on one page are consumed in
XML order.

### Edge cases

| Situation | Behaviour |
|---|---|
| Key answer is not in the region's `charSet` (e.g. `Z` against `ABCD`) | That option is skipped and logged; if nothing is left the question stays blank |
| Key lists a question beyond the sheet's question count | Simply unused |
| Key is shorter than the question count | The rest follow the **Other questions** setting |
### Format warning before a run
`ParseAnswerKey(input, out problems)` reports anything that looks like a typo:

| Input | Reported |
|---|---|
| `x-A` | *the part before the dash must be a question number, like 3-B* |
| `0-A` | same — question numbers start at 1 |
| `1-A,1-B` | *Question 1 is listed more than once; the last entry wins* |
| `1-A.` | *'.' is not a plain answer - use letters or digits only* |

The hint under the key box turns **red** and shows the first problem live, and
`MainForm.ConfirmAnswerKeyProblems` puts them in a Yes/No dialog when **Process
OMR** is pressed — defaulting to **No**, so a typo cannot start a batch by
accident. The key still parses and runs if the user says Yes; the warning does
not change what gets drawn.

### Export (`answer_key_<timestamp>.csv`)
Controlled by the *Export* `ToggleSwitch` (`OmrBatchOptions.ExportAnswerKey`,
**default off** - switch it on when you want the CSV). After a batch,
`WriteAnswerKeyCsv` writes what was **actually**
filled into the output folder — the ground truth for checking an OMR scoring
pipeline. The path comes back on `OmrBatchResult.AnswerKeyFile` and is shown in
the status bar.

```
OutputFile,Roll,Registration,PageId,Region,Question,Answer
sheet01_edit_145947_996.png,10205026680,1607495,1,answer,1,A
sheet01_edit_145947_996.png,10205026680,1607495,1,answer,2,C
sheet01_edit_145947_996.png,10205026680,1607495,1,QuestionQuality,1,S
```

Notes:
- Randomly picked **Question Quality** values are recorded too, distinguished by
  the `Region` column, so that random choice is also verifiable.
- The file is timestamped rather than overwritten, so a previous run's ground
  truth is never destroyed.
- Written as UTF-8 **with BOM** so Excel opens it without mangling values.
- Questions left blank (key answer outside the `charSet`) produce **no row** —
  a gap in the `Question` sequence marks an unanswered bubble.

---

## 8. Excel / CSV Loader (`ExcelLoader.cs`)

### Public API

```csharp
List<Dictionary<string, string>> ExcelLoader.LoadRows(string filePath);
List<Dictionary<string, string>> ExcelLoader.LoadRows(string filePath, out List<string> headers);
```

Returns rows as dictionaries (header → cell value). Header lookup is
**case-insensitive**. The second overload also reports the column headers **in
sheet order** — `Dictionary` makes no promise about key order, so the column
pickers need the list separately. Blank and duplicate headers are dropped.

### Supported formats
- `.csv` — UTF-8 text, comma-separated, simple quote handling (`""` for embedded quotes)
- `.xlsx` — Office Open XML (zip)

### XLSX parsing (no NuGet)
1. Open the file with `ZipFile.OpenRead`.
2. Read shared strings from `xl/sharedStrings.xml` into `string[]`.
3. Find the worksheet (`xl/worksheets/sheet1.xml`, or first matching `xl/worksheets/*.xml`).
4. For each `<row>`:
   - For each `<c>` (cell), determine column index from `r="A1"`-style ref via `ColumnRefToIndex`.
   - Pad missing columns with empty strings.
   - Resolve value:
     - `t="s"` → shared string lookup
     - `t="inlineStr"` → concat all `<is><t>...</t></is>` children
     - else → take `<v>` text directly (numbers stored as digit strings)
5. First row is treated as header; subsequent rows become dicts.

### Column picker (`ColumnMapDialog.cs`)

Importing always opens a small dialog rather than trusting the header matcher:

- Two `DropDownList` combos, one for roll numbers and one for registrations,
  listing every column in sheet order plus **(do not import)**.
- Auto-detection (`FindMatchingHeader`) only **seeds** the two combos, so a
  sheet with columns like *Candidate ID* / *Enrolment Ref* — which the matcher
  can never guess — is still usable.
- Under each combo a live preview shows the value count and the first three
  values, so the choice can be checked before importing.
- **(do not import)** on a field leaves that text box untouched; picking
  neither is refused with a nudge rather than importing nothing.

### MainForm integration (`LoadStudentDataFromFile`)
- File dialog filtered to `.xlsx` and `.csv`
- After loading, calls `FindMatchingHeader` with normalized matching (strips spaces/underscores, lowercases) against candidate header names:
  - **Roll**: `Roll Number`, `Roll No`, `RollNumber`, `RollNo`, `Roll`
  - **Reg**: `Registration Number`, `Registration No`, `RegistrationNumber`, `RegistrationNo`, `Registration`, `Reg Number`, `Reg No`, `Reg`
- Shows `ColumnMapDialog` seeded with those matches; the user confirms or
  changes them, and Cancel aborts the import completely.
- Joins the chosen column''s values with `,` and writes them to the
  corresponding TextBoxes.
- Shows count summary in the StatusStrip (e.g. *"Loaded 14 roll(s) and 14 registration(s) from students.xlsx"*).
- If no expected header is found, shows a warning MessageBox and aborts.

---

## 9. Output Result Dialog (`OutputResultDialog.cs`)

Shown after a successful batch (failure flow keeps the original error MessageBox).
Styled with the same `UiTheme` primitives as the main window.

Contents:
- **Result badge** — MDL2 `Completed` glyph in green, or `Warning` in amber
- **Title** — *Processing complete* / *Completed with failures*
- **Summary** — `X sheet(s) filled · Y failed`
- **Output folder** — read-only `TextBox` inside an `InputHost`, auto-`SelectAll()`
  on click/focus so the user can `Ctrl+C`
- **Copy path button** — calls `Clipboard.SetText`, button text becomes `Copied!`
  for ~1.4s then resets (Timer-based)
- **Open folder button** — launches Explorer at the path
- **Close button** — primary style, bound to both `AcceptButton` and
  `CancelButton` (so Esc and Enter both close) and set as `ActiveControl` so the
  dialog does not open with the path box highlighted

Dialog is `FixedDialog` border, `CenterParent`, no taskbar entry.

---

## 10. Build & Run

### Command line
```bash
MSBuild DynamicOmrFiller.csproj -t:Rebuild -p:Configuration=Release -v:minimal
```

Output: `bin/Release/DynamicOmrFiller.exe` (~42 KB self-contained — no external DLLs needed beyond the .NET Framework runtime).

### Visual Studio
Open `DynamicOmrFiller.sln`, set `DynamicOmrFiller` as startup project, press **F5**.

### .csproj references (in addition to default WinForms set)
- `System.Xml.Linq` — XML parsing
- `System.IO.Compression` + `System.IO.Compression.FileSystem` — XLSX zip reading

---

## 11. Extending the Project

### Adding a new region MemberName
1. In `OmrProcessor.ProcessOmrImage`, add a new `else if` branch in the dispatch chain.
2. Implement a `Draw{Foo}` method using existing helpers:
   - `grid.XLines[region.StartX - 1]`, `grid.YLines[region.StartY - 1]` — origin
   - `grid.StepX`, `grid.StepY` — cell size
   - `region.PaddingX/Y`, `region.SpacingX/Y` — offsets/gaps
   - `DrawDot(g, x, y, stepX, stepY)` — render a black ellipse
3. (Optional) If your region's `charSet` is comma-separated tokens, use `ParseCharSet`.

### Adding a new user option (UI control + processing flag)
1. Add a `public` property to `OmrBatchOptions` (in `OmrModels.cs`).
2. Add a private field on `OmrProcessor` (e.g. `_myFlag`).
3. Set it in `ProcessBatch`: `_myFlag = options.MyFlag;`.
4. Use it in your `Draw*` method or dispatch branch.
5. Add a UI control in MainForm — typically inside `BuildSettingsGroup` for booleans/numerics, or its own group for larger features. Wire its value into `CollectOptions()`.

### Adding a new file format for the loader
Extend `ExcelLoader.LoadRows` with a new extension branch and a parser that returns `List<List<string>>` (rows × cells), then call `BuildRows` to convert to dict rows.

---

## 12. Known Quirks / Things to Watch

- **`Program.cs` is dead code** — original console prototype with duplicate type definitions in namespace `OmrSmartBatchSystem`. Not in the `.csproj` compile list. Safe to delete; kept for now as a reference.
- **Marker detection thresholds are hard-coded** for ~36px markers and dark-pixel cutoff `< 80`. Different scanner DPI may need tuning in `FindMarkerRect`.
- **Multi-page roll alignment** depends on `DetectPageIdFromImage`. The fallback when no bubble exceeds `BlackPixelPercent` is `defaultValue` (else `"1"`), which can still mask a detection failure on a *bubbled* multi-page design and misalign rolls. A page id with no matching `<omr>` section now throws rather than drawing nothing, so the common failure is loud; a *wrong but existing* page id is still silent.
- **`LockBits`/`UnlockBits` not in try/finally** in `FindMarkerRect` and `GetDarknessPercentage` — a throw inside the `unsafe` block would leak a locked bitmap. No known crash path, but defensive `try/finally` would be safer.
- **No cancellation** for long batches — once "Process OMR" is clicked, the user can only wait or kill the app.
- **CSV split is naive-ish** — handles quoted fields with `""` escapes, but doesn't handle embedded newlines inside quoted fields. Fine for the rolls/reg use case (numeric IDs).

---

## 13. Version History

Tracked in `Properties/AssemblyInfo.cs` (`AssemblyVersion` + `AssemblyFileVersion`). Displayed at runtime in the title bar and in the header version pill.

| Version | Highlights |
|---|---|
| **1.0.0** | Initial WinForms version (clean, GroupBox layout). Replaces the legacy `Program.cs` console prototype. Drag-drop on path inputs. |
| **1.1.0** | Layout split into Source files / Student / SAQ / Settings sections. Auto output folder (`FillUpOMR/`) when output is empty. SAQ font size control. "Open output folder" checkbox. QuestionQuality random fill (initially via checkbox). |
| **1.1.1** | QuestionQuality checkbox restored with **default checked** (out-of-box random fill). |
| **1.2.0** | Excel/CSV bulk import for Roll/Registration via `ExcelLoader.cs` (built-in zip + XML parser, no NuGet). Compact SAQ text height. |
| **1.2.1** | Custom result dialog (`OutputResultDialog`) with copyable path TextBox, Copy-path button, Open-folder button. |
| **1.2.2** | This documentation file added. |
| **1.3.0** | **UI redesign.** Card-based flat shell in `UiTheme.cs`: rounded cards, focus-aware inputs, rounded buttons, segmented input-mode selector, toggle switches, fixed footer action bar with slim indeterminate progress and a colour-coded status dot, header with app-icon tile and version pill. `OutputResultDialog` restyled to match. `app.manifest` added for system DPI awareness (sharp rendering on scaled displays). |
| **1.4.0** | **MCQ answer key.** New *MCQ answer key* card: type the exact MCQ answers (`A,C,B,D` or `ACBD`) and the filler uses them instead of random picks; empty keeps the old random behaviour. Key applies to `answer*` regions only. New *Export* toggle writes `answer_key_<timestamp>.csv` (file, roll, registration, page, region, question, answer) into the output folder as ground truth for the scoring pipeline. |
| **1.4.1** | Card headers are now collapse / expand toggles. *Student information*, *MCQ answer key* and *SAQ content* start collapsed so the whole form fits without scrolling; *Source files* and *Settings* stay open. The answer-key *Export* toggle now defaults to **off**. |
| **1.5.0** | **Per-question answer key.** New format `1-A,2-AC,3-B,4-ABCD` — explicit question numbers, and several letters bubble **several options on one question**. An empty slot (`,,`) blanks that question. New *Other questions* toggle decides whether questions the key does not list are filled randomly (default) or left blank. `ParseAnswerKey` now returns `Dictionary<int, string[]>`; the old `A,C,B,D` and `ACBD` forms still work. |
| **1.5.1** | The *Settings* card also starts collapsed - only *Source files* is open on launch. |
| **1.5.2** | The window now auto-fits its height to the cards (`FitToContent`), so collapsing or expanding a card resizes the shell instead of leaving empty space below the last card. |
| **1.5.3** | Roll numbers and Registration are multiline text areas instead of single-line boxes; `OmrProcessor.SplitCsv` now accepts newlines, semicolons, tabs and spaces as separators alongside commas, so one-value-per-line input works. |
| **1.5.4** | Live count under the roll / registration boxes, using the processor's own splitter; unequal non-zero counts are flagged amber before the batch runs. `SplitCsv` is now the public `OmrProcessor.SplitValues`. |
| **1.6.0** | **Fixes found against a real sheet** (`College_Admission_MCQ_20_SAQ_5`). `<omrInfo isVirtual="true" defaultValue="2">` is now honoured — previously the page id always resolved to `"1"`, no `<omr omrNo="1">` section existed, **no region was drawn at all** and the batch still reported success. New-student detection uses `FirstPageId` instead of the literal `"1"`, so single-page designs advance the roll per sheet. `lineStart` now sets an answer region's question numbers. A missing `<omr>` section, or a page where nothing could be filled, throws instead of silently producing an untouched copy; the log adds the detected grid size, per-region skips and `Regions filled: N of M`. |
| **1.7.0** | **Answer key format warning** — typos like `x-A`, `0-A`, `1-A,1-B` or `1-A.` are reported live under the key box and in a Yes/No dialog (defaulting to No) before a batch starts. **Saved defaults** — *Save as default* buttons on the Student and SAQ cards persist roll/registration and the SAQ text + sizes + position to `%APPDATA%\DynamicOmrFiller\settings.xml`, loaded on the next launch. **SAQ label size** is now controllable (it was fixed before, which made the existing text-size control look broken), and **SAQ text position** is settable left/centre/right and top/middle/bottom. |
| **1.8.0** | **Written roll / registration digits.** `DrawWrittenNumber` prints each digit in the write-in box above its own bubble column, so the output resembles a hand-filled sheet. Controlled by a *Write numbers* toggle in the Student card (**default on**), saved with the student defaults. |
| **1.9.0** | **Write offset** — a spin box (grid rows, 0-20, 0.1 steps, default 1.0) controls how far above the bubbles the written digits sit, so they can be nudged to fit a sheet whose write-in row sits elsewhere. Saved with the student defaults; greyed out while *Write numbers* is off. |
| **1.10.0** | **Column picker for Excel/CSV import.** A new `ColumnMapDialog` lets the user choose which column holds the roll numbers and which holds the registrations, with a live preview of each column's values. Header auto-detection now only seeds the pickers, so files with unguessable column names import fine; previously such a file was rejected outright. `ExcelLoader.LoadRows` gained an overload that reports headers in sheet order. |
| **1.11.0** | **PDF input.** A third *PDF file* mode renders every page with the Windows PDF engine (late-bound, no SDK or NuGet) and fills the ones that match the XML; question pages are skipped and counted separately. **Half-page sheets now work**: the bottom fiducial is searched below the top marker instead of in a fixed bottom quarter, and candidates must match the top marker's size - a full A4 page of `Engineering 20+1 A4` went from a bogus 16x12 grid to the correct 57x31. The detected grid is also checked against the XML's own `gridX`/`gridY`. **An unreadable page-number bubble no longer defaults to page 1**, which was drawing MCQ dots onto written-answer sheets. |
| **1.11.1** | **Page-id marks are sized from `resultCountCellX/Y`.** They are not always a full grid cell, so the pitch between candidates is `(cell + spacing)` and the sample point is the cell's centre. On the Engineering 20+1 sheet the marks sit one row apart (`resultCountCellY=".5"`), but the old full-cell assumption sampled 1.5 rows apart and hit the gap between them: page 2 read as unidentifiable and failed. Page 2 now reads as page 2, keeps the same student's roll and gets its `SaqAnswer1` filled. The same bug was behind the blank Engineering template PDF yielding nothing. |
| **1.11.2** | **PDF pages render at the size asked for.** Windows scales `DestinationWidth` by the monitor's DPI factor, so a 125% display produced 3100px pages instead of 2480px and the detected grid step changed with the monitor rather than the document. `PdfPageRenderer.RenderPage` now rescales to the exact width. |
| **1.12.0** | **PDF output is cropped to the OMR sheet.** A PDF page is the whole A4 sheet, so a half-page design kept the question paper in the output. `CropToSheet` trims to the fiducial markers' bounding box (new `GridSystemData.SheetBounds`) plus two grid rows, landing within ~20 px of the hand-cropped sheets. Toggle in Settings, default on; image and folder modes are untouched. |
| **1.13.0** | **The SAQ cell label can be turned off.** New *Cell label* toggle in the SAQ card (`OmrBatchOptions.WriteSaqLabel`, default on): off stops `SaqAnswer1` being printed and hands the whole cell to the SAQ text instead of reserving an empty strip for it. *Label size* greys out while it is off. Saved with the SAQ defaults; a settings file written before 1.13.0 reads back as on. |

### Workflow conventions for future changes
- **Bump `AssemblyVersion`** in `Properties/AssemblyInfo.cs` on every change set (minor for features, patch for fixes/docs).
- **Run a Release rebuild** at the end of each task: `MSBuild DynamicOmrFiller.csproj -t:Rebuild -p:Configuration=Release -v:minimal`.
- **Confirm 0 errors / 0 warnings** before reporting done.
