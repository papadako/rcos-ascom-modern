# RCOS TCC — Modern ASCOM Driver Starter

This starter turns the legacy VB6 RCOS TCC code into a modern C# codebase:

- `RCOS.Tcc/` — a **pure C#** serial protocol client you can reuse anywhere.
- `ASCOM.RCOS.Focuser/` — IFocuserV3 mapped to `TccClient` focuser functions
- `ASCOM.RCOS.Rotator/` — IRotatorV3 mapped to rotator functions
- `ASCOM.RCOS.Fans/` — ISwitchV2 for Fan Auto/Off booleans and Fan Speed (0..1)
- `ASCOM.RCOS.Dew/` — ISwitchV2 for Secondary Heater (Auto/Off, Manual Power, Setpoint) and Dew1/Dew2 power
- `ASCOM.RCOS.TemperatureSensor/` — ITemperatureSensor exposing Ambient, Primary, Secondary, Electronics temps (°C)
- `RCOS.DriverCommon/` — contains a minimal profile
 
> The skeletons purposely avoid direct ASCOM references so you can add the exact versions that match your system (ASCOM Platform 6.x+).

## What’s already mapped

From the legacy VB6 classes, now implemented in `RCOS.Tcc.TccClient` with a background parser and **XOn/XOff** serial flow control:

### Focuser (`RCOS.cls`)
- **Step size:** `0.5625 µm`
- **Max step:** `40000`
- **Status / queries:** send `" Q "`.  
  Parser handles tokens:
  - `:s` — set position
  - `:a` — actual position
  - `:h` — home status
- **TempComp commands:**
  - `+0` — off
  - `+1` — auto
  - `+2` — manual
- **Note:** Absolute/relative move verbs are placeholders — update to match your firmware.

### Rotator (`Rotator.cls`)
- **Steps ↔ Degrees:** `200 steps = 1°`
  - `:rs` — set position
  - `:rt` — actual position
- **Home / query:**
  - `" r "` — home
  - `" R "` — query

### Temperature / Fans / Dew (`Temperature.cls`)
- **Temperatures:**
  - `:t1` — ambient
  - `:t2` — primary
  - `:t3` — secondary
  - `:t7` — electronics  
  *(values are hundredths, divide by 100)*
- **Fan control:**
  - Mode: `:fm` (0 = manual, 1 = auto, 2 = off)
  - Speed: `:fs` (0–100)
  - Commands:
    - `"n1 "` — auto
    - `"n2 "` — off
    - `"y{0..100} "` — manual speed
  - **Tuning:**
    - `:fg` — gain ×10 → `" g{×10} "`
    - `:ft` — deadband ×10 → `" O{×10} "`
- **Secondary (dew) heater:**
  - Mode: `:sm` (0 = manual, 1 = auto, 2 = off)
    - `"w1 "` — auto
    - `"w2 "` — off
  - Manual power: `:ss` (0–100) → `"s{0..100}"`
  - Setpoint: `:st` (tenths °C, −10..+10 °C) → `"P{±100}"`
- **Additional dew channels:**
  - `:d1` — Dew1 power % → `"c{0..100}"`
  - `:d2` — Dew2 power % → `"k{0..100}"`

### General
- **Ping:** `"! "` expecting token `:!`
- **Firmware version:** `:vr`
- **Serial settings:** 9600 8N1, **XOn/XOff** flow control (matches VB6)

These are implemented in `RCOS.Tcc.TccClient` with a background parser.

## Profile wiring

All drivers load/save settings via **`RCOS.DriverCommon.DriverProfile`**:

- **Keys:** `ComPort`, `FocuserMaxStep`, `FocuserStepSizeMicrons`, `FanAutoOnConnect`
- **Backends:**
  - JSON fallback at `%APPDATA%\RCOS\ASCOM\<DeviceType>\profile.json` (works immediately)
  - ASCOM Profile store when you add **ASCOM.Utilities** and compile with `USE_ASCOM`
- Each driver exposes:
  - `DriverId` — unique ASCOM ProgID for the driver
  - `PortName` — reads/writes profile directly

## How to finish the ASCOM drivers

1. **Install ASCOM Platform (6.x or newer)** on your dev machine.
2. **Add ASCOM references** to the driver projects (`ASCOM.DeviceInterface`, `ASCOM.Utilities`).  
   - You can add via NuGet (if available) or as direct references from the ASCOM installation.
3. **Uncomment** the interface implementations and fill in the remaining required members (e.g., `SetupDialog`, `Connected`, `Link`, `Profile` persistence, COM registration attributes).
4. **Register for COM** (classic) or implement **Alpaca** hosting, depending on your preferred distribution target.
5. **Test** with ASCOM Conform and client apps (e.g., NINA, SGP).

### Classic COM vs Alpaca

- If you need maximum compatibility with Windows imaging apps today, ship **classic COM** drivers.
- If you want cross-platform and modern transport, wrap the same `TccClient` into **Alpaca** devices using ASCOM’s Alpaca libraries (recommended long-term).

## IMPORTANT — Firmware commands

Some motion commands in the legacy VB6 weren’t explicitly present in the snippets for **absolute/relative** moves. The starter uses **placeholder** move encodings (`"m±{steps}"` and `"r±{steps}"`). If your TCC firmware expects different verbs, change the strings in `TccClient.MoveFocuserRelative` and `TccClient.MoveRotatorRelativeDeg` accordingly.

Use the parser (see `RawTokens` queue) to inspect the exact replies from your device as you test.

## Build

```bash
dotnet build src/RCOS.Tcc/RCOS.Tcc.csproj
# Drivers target net48 to align with ASCOM COM: build them from Visual Studio once references are added.
```

## Where this came from

- The mapping was derived from your VB6 files at https://sourceforge.net/projects/rcostccvb6/files/:
  - `Serial.cls` (MSCOMM serial wrapper)
  - `RCOS.cls` (Focuser logic)
  - `Rotator.cls` (Rotator logic)
  - `Temperature.cls` (Temps, fans)

## Next steps

- Wire-up **Profile** (COM port, step limits, temp-comp options) and **SetupDialog**.
- Add **CoverCalibrator** and/or **Switch** devices for cover, dew, and limit switch bits.
- Produce signed **MSI installers** and **ASCOM registration** scripts.

