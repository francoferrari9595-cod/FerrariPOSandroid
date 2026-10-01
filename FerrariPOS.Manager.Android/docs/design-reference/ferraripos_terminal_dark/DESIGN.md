---
name: FerrariPOS Terminal Dark
colors:
  surface: '#0b1326'
  surface-dim: '#0b1326'
  surface-bright: '#31394d'
  surface-container-lowest: '#060e20'
  surface-container-low: '#131b2e'
  surface-container: '#171f33'
  surface-container-high: '#222a3d'
  surface-container-highest: '#2d3449'
  on-surface: '#dae2fd'
  on-surface-variant: '#e6bdb8'
  inverse-surface: '#dae2fd'
  inverse-on-surface: '#283044'
  outline: '#ac8884'
  outline-variant: '#5c403c'
  surface-tint: '#ffb4ab'
  primary: '#ffb4ab'
  on-primary: '#690005'
  primary-container: '#dc2626'
  on-primary-container: '#fff6f5'
  inverse-primary: '#bf0715'
  secondary: '#4edea3'
  on-secondary: '#003824'
  secondary-container: '#00a572'
  on-secondary-container: '#00311f'
  tertiary: '#8bceff'
  on-tertiary: '#00344e'
  tertiary-container: '#0079af'
  on-tertiary-container: '#f4f9ff'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#ffdad6'
  primary-fixed-dim: '#ffb4ab'
  on-primary-fixed: '#410002'
  on-primary-fixed-variant: '#93000b'
  secondary-fixed: '#6ffbbe'
  secondary-fixed-dim: '#4edea3'
  on-secondary-fixed: '#002113'
  on-secondary-fixed-variant: '#005236'
  tertiary-fixed: '#c9e6ff'
  tertiary-fixed-dim: '#8bceff'
  on-tertiary-fixed: '#001e2f'
  on-tertiary-fixed-variant: '#004b6f'
  background: '#0b1326'
  on-background: '#dae2fd'
  surface-variant: '#2d3449'
typography:
  display-kpi:
    fontFamily: Inter
    fontSize: 36px
    fontWeight: '700'
    lineHeight: 44px
    letterSpacing: -0.02em
  display-kpi-mobile:
    fontFamily: Inter
    fontSize: 28px
    fontWeight: '700'
    lineHeight: 34px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '700'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  headline-sm:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-numeric:
    fontFamily: JetBrains Mono
    fontSize: 13px
    fontWeight: '500'
    lineHeight: 18px
    letterSpacing: 0.02em
  label-kpi-badge:
    fontFamily: JetBrains Mono
    fontSize: 11px
    fontWeight: '600'
    lineHeight: 14px
    letterSpacing: 0.04em
  label-action:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
    letterSpacing: 0.01em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1rem
  margin: 1rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 0.75rem
  space-lg: 1rem
  space-xl: 1.5rem
---

## Brand & Style

This design system delivers a mission-critical, high-contrast dark environment engineered for commercial retail managers and warehouse operators. The aesthetic combines industrial utility with precision performance: deep slate substrates, razor-sharp typography, tactile controls, and high-visibility status telemetry.

The target users operate under demanding conditions—fluorescent warehouse lighting, dim back offices, and fast-paced retail checkout counters. The interface must provide instant visual triage, zero lag perception, and confident single-handed touch accuracy. The emotional signature is fast, authoritative, industrial, and hyper-reliable.

## Colors

The palette is anchored in deep cool slates to eliminate glare while ensuring deep contrast for long shifts:

- **Primary (`#DC2626` / `#EF4444`)**: Ferrari Racing Red. Reserved for high-priority actions, critical stock alerts, system disconnect states, and branded master actions.
- **Secondary (`#10B981`)**: Emerald Success. Dedicated exclusively to positive financial metrics (gross sales, cash register surplus, active transactions, and online node status).
- **Tertiary (`#009EE3`)**: Gateway Cyan/Blue. Identifies digital payment methods (Mercado Pago, card sweeps, digital receipts).
- **Warning (`#F59E0B`)**: Amber Signal. Flags low inventory thresholds, pending credit debts, and draft tickets.
- **Neutral Base (`#0F172A`) & Elevate (`#1E293B` / `#334155`)**: Slate foundation layers providing spatial hierarchy without heavy cast shadows.
- **On-Surface Text**: `#F8FAFC` (900 grade) for primary metric data and `#94A3B8` (400 grade) for operational sub-labels.

## Typography

Typography prioritizes tabular legibility and instant glanceability. **Inter** handles all narrative hierarchy and operational UI labels, leveraging its balanced vertical metrics and clear aperture rendering on mobile OLED screens.

**JetBrains Mono** is introduced specifically for SKUs, currency amounts, barcode integers, and network telemetry. Monospaced tabular figures prevent visual jittering when sales totals and network ping rates update live via WebSockets.

## Layout & Spacing

The layout is built for single-handed thumb-zone reachability on vertical Android handhelds and rugged POS terminals (5.5" to 6.7").

- **Grid Model**: 4-column fluid layout on mobile viewports with `16px` (`1rem`) outer margins and gutters. On tablet and docked terminal displays (600dp+), the layout reflows into an 8-column master-detail structure.
- **Ergonomic Safe Zones**: The top 25% of the screen is strictly reserved for passive monitoring (sync status pills, aggregated revenue metrics). Interactive controls, primary scan inputs, and navigation actions populate the lower 50% "reach zone".
- **Density & Touch Targets**: Component tap targets never drop below `48dp` x `48dp` (with standard buttons scaled to `56dp` height) to ensure error-free physical operation during warehouse audits and rapid register checks.

## Elevation & Depth

Visual hierarchy relies on calibrated surface luminance and low-contrast perimeter strokes rather than muddy black drop-shadows:

- **Surface 0 (Canvas Base)**: `#0F172A` — App background, behind all scrolls.
- **Surface 1 (Card & Module Containers)**: `#1E293B` — Used for KPI tiles, transaction line items, and section blocks. Outlined with a 1px crisp stroke in `#334155` at 60% opacity.
- **Surface 2 (Floating Overlays & Menus)**: `#334155` — Elevated dialogs, barcode modal sheets, and dropdown popovers. Accented by a 1px top highlight (`rgba(255, 255, 255, 0.08)`).
- **Surface 3 (Bottom Navigation & Sticky Headers)**: `#0F172A` at 92% opacity with `backdrop-filter: blur(12px)` and a subtle `1px` border separation (`#1E293B`).
- **Glow & Active States**: Urgent warnings and critical Ferrari red actions emit an inner ambient glow (`0 0 12px rgba(220, 38, 38, 0.25)`) instead of traditional drop shadows to indicate live hardware connection or immediate user intervention.

## Shapes

The design system adopts a balanced structural geometry (Level 2 - Rounded). Standard cards, form fields, and tactile touch buttons carry an `8px` (`0.5rem`) corner radius. This provides a modern, friendly feel while maintaining an authoritative commercial profile.

Status pills, sync indicators, and floating scanner toggles utilize `rounded-full` (`9999px`) geometry to distinctly separate informational meta-badges from actionable functional cards and inputs.

## Components

### Buttons
- **Primary / Sale Actions**: Solid Ferrari Red (`#DC2626`), high-contrast white text, minimum height `52dp`, `8px` corner radius. Active state scales slightly down (`0.98`) with a tactile background shift to `#B91C1C`.
- **Secondary / Utility**: Surface 1 background (`#1E293B`) with a `1px` border in `#334155` and text in `#F8FAFC`.
- **Destructive / Void**: Outlined with `#EF4444` and red tint hover (`rgba(239, 68, 68, 0.1)`).

### Live Telemetry Badge (FerrariPOS PC Sync)
- Pill-shaped container (`rounded-full`) positioned at top right. Background `#064E3B` with `#6EE7B7` text and a pulsing circular indicator in Emerald (`#10B981`) denoting real-time local LAN/WiFi connection. Shifts to Amber (`#F59E0B`) during syncing and solid Red (`#DC2626`) if disconnected.

### Metric & KPI Cards
- Dark Slate container (`#1E293B`), `8px` border-radius, `1px` border (`#334155`).
- Displays monospaced currency values (`display-kpi-mobile`) with dynamic color indicators: Emerald (`#10B981`) for positive cashflow, Cyan (`#009EE3`) for Mercado Pago volume, and Amber (`#F59E0B`) for pending credit.

### Quick Search & Barcode Scanner Bar
- Input field fixed at `52dp` height, background `#1E293B`, border `#334155`.
- Integrated right-aligned camera icon button (`44dp` touch box) highlighted with a subtle border and red camera icon, allowing instant barcode activation without keyboard launch.

### Transaction & Inventory Lists
- Divided rows separated by `1px` borders (`#1E293B`), eliminating nested visual friction. Monospaced SKU codes and pricing aligned right for vertical optical comparison.

### Bottom Navigation Bar
- Grounded bar (`64dp` height) with frosted slate surface (`#0F172A` at 95% opacity). Icons sized at `24dp` with `label-kpi-badge` captions. Active tab highlights in Ferrari Red (`#EF4444`) with an active indicator bar above the tab item.