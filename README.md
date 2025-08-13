# RCOS TCC — Modern ASCOM Driver Starter

This starter turns the legacy VB6 RCOS TCC code into a modern C# codebase:

- `RCOS.Tcc/` — a **pure C#** serial protocol client you can reuse anywhere.
- `ASCOM.RCOS.Focuser/` — a minimal **IFocuserV3** driver skeleton for the focuser.
- `ASCOM.RCOS.Rotator/` — a minimal **IRotatorV3** driver skeleton for the rotator.
- `ASCOM.RCOS.Switch/` — a minimal **ISwitchV2** driver skeleton for fan mode and speeds.
- `ASCOM.RCOS.Dew/` — a minimal **ISwitchV2** driver skeleton for dew and secondary heater.
- `ASCOM.RCOS.Temperature/` — a minimal **ITemperatureSensor** driver skeleton for TCC temperatures.
- `RCOS.DriverCommon/` — contains a minimal profile.
 
> The skeletons purposely avoid direct ASCOM references so you can add the exact versions that match your system (ASCOM Platform 6.x+).

## What’s already mapped

From the legacy VB6 classes:

- **Focuser (RCOS.cls)**  
  - Step size: **0.5625 µm**  
  - Max step: **40000**  
  - Queries use `" Q "`; parser handles tokens `:s` (set pos), `:a` (actual pos), `:h` (home).  
  - TempComp commands: `"+0"` (off), `"+1"` (auto), `"+2"` (manual).  
- **Rotator (Rotator.cls)**  
  - Step conversion: **200 steps = 1 degree** (`:rs` set pos, `:rt` actual pos).  
  - Home: `" r "`; Query: `" R "`.  
- **Temperature / Fans (Temperature.cls)**  
  - Temps: `:t1` ambient, `:t2` primary, `:t3` secondary, `:t7` electronics; values are **hundredths** (divide by 100).  
  - Fan mode: `:fm` (0=manual, 1=auto, 2=off). Fan speed: `:fs` (0..100).  
  - Commands: `"n1 "` (auto), `"n2 "` (off), `"y{speed} "` (manual speed).  
- **General**  
  - Ping: `"! "` expecting token `:!`.  
  - Firmware version: token `:vr`.

These are implemented in `RCOS.Tcc.TccClient` with a background parser.

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

- The mapping was derived from your VB6 files:
  - `Serial.cls` (MSCOMM serial wrapper)
  - `RCOS.cls` (Focuser logic)
  - `Rotator.cls` (Rotator logic)
  - `Temperature.cls` (Temps, fans)

## Next steps I can do for you

- Wire-up **Profile** (COM port, step limits, temp-comp options) and **SetupDialog**.
- Add **CoverCalibrator** and/or **Switch** devices for cover, dew, and limit switch bits.
- Produce signed **MSI installers** and **ASCOM registration** scripts.

## Temperature & Fan (added)

- **ASCOM.RCOS.TemperatureSensor/** — maps `:t1/:t2/:t3/:t7` to Celsius readings (Ambient, Primary, Secondary, Electronics).
- **ASCOM.RCOS.Switch/** — exposes fan control:
  - Switch[0] **Fan Auto** (bool) → `n1`
  - Switch[1] **Fan Off** (bool) → `n2`
  - Switch[2] **Fan Speed** (0..1) → `y{0..100}` (forces Manual mode)

> After adding ASCOM references, uncomment the interface lines and fill in the standard members (SetupDialog, Connected, Profile).



## Dew / Secondary Heater

- **ASCOM.RCOS.Dew/** — exposes dew-related controls via ISwitchV2:
  - Secondary heater **Auto/Off** booleans.
  - Secondary heater **Manual Power** (0..1) and **Setpoint** (-10..+10 °C mapped to 0..1).
  - **Dew1** and **Dew2** power (0..1).
- Protocol:
  - Tokens: `:sm` (mode), `:ss` (sec power), `:st` (setpoint), `:d1`, `:d2` (dew channels), plus `:fg` and `:ft` for fan tuning.
  - Commands: `w1`/`w2` (sec auto/off), `s{0..100}` (sec manual power), `P{±100}` (setpoint in tenths °C),
    `c{0..100}` (dew1 power), `k{0..100}` (dew2 power), `g{1..100}` gain x10, `O{0..100}` deadband x10.
