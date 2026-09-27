"""Reproduce the celestial study's illustrative tables and scientific figure.

Research only: not a runtime ephemeris, atmosphere implementation or measured data.
Python 3; CSVs use only the standard library. The figure requires matplotlib.
See README.md for sources, units, assumptions and limitations.
"""
from __future__ import annotations

import csv
import math
from pathlib import Path

OUT = Path(__file__).resolve().parent
SUN_RADIUS_DEG = 1919 / 3600 / 2


def airmass(apparent_elevation_deg: float) -> float:
    h = apparent_elevation_deg
    if not 0 <= h <= 90:
        raise ValueError("Kasten-Young sample must be above the apparent horizon")
    return 1 / (math.sin(math.radians(h)) + 0.50572 * (h + 6.07995) ** -1.6364)


def refraction_deg(geometric_elevation_deg: float) -> float:
    """NOAA piecewise approximation; not a production first-contact solver."""
    h = geometric_elevation_deg
    if h > 85:
        return 0
    if h > 5:
        t = math.tan(math.radians(h))
        return (58.1 / t - 0.07 / t**3 + 0.000086 / t**5) / 3600
    if h > -0.575:
        return (1735 - 518.2*h + 103.4*h*h - 12.79*h**3 + 0.711*h**4) / 3600
    return -20.774 / math.tan(math.radians(h)) / 3600


def vertical_ratio(h: float) -> float:
    r = SUN_RADIUS_DEG
    return (2*r + refraction_deg(h+r) - refraction_deg(h-r)) / (2*r)


def solar_elevation(minutes: float, declination_deg: float, latitude_deg: float = 38.72) -> float:
    """Constant-declination illustration, 15 deg/hour, start at center -0.8333 deg."""
    p, d, h0 = map(math.radians, (latitude_deg, declination_deg, -0.8333))
    argument = (math.sin(h0) - math.sin(p)*math.sin(d)) / (math.cos(p)*math.cos(d))
    hour_angle_at_rise = -math.acos(argument)
    hour_angle = hour_angle_at_rise + math.radians(minutes * 0.25)
    return math.degrees(math.asin(math.sin(p)*math.sin(d) + math.cos(p)*math.cos(d)*math.cos(hour_angle)))


def artistic_gain(h: float) -> float:
    t = min(1, max(0, h / 25))
    return 1 + 2.2 * (1 - t*t*(3-2*t))


def lunar_phase_flux(alpha_deg: float) -> float:
    """Relative to the empirical alpha=0 extrapolation, WITHOUT opposition surge."""
    return 10 ** (-0.4 * (0.026 * abs(alpha_deg) + 4e-9 * alpha_deg**4))


def current_rgb_transmission(h: float) -> tuple[float, float, float]:
    """Diagnostic reproduction of OceanLightingModel.cs at commit 9d270a6.

    Straight ray, no refraction, current fixed density profiles and RGB coefficients.
    This is NOT a calibrated spectral reference or an RGB palette to ship.
    """
    start, top = 6360.002, 6460.0
    mu = math.sin(math.radians(h))
    distance = -start*mu + math.sqrt(start*start*(mu*mu-1) + top*top)
    rayleigh = mie = ozone = 0.0
    for i in range(64):
        a, b = distance*(i/64)**2, distance*((i+1)/64)**2
        t = (a+b)/2
        altitude = max(0, math.sqrt(start*start+t*t+2*start*t*mu)-6360)
        rayleigh += math.exp(-altitude/8)*(b-a)
        mie += math.exp(-altitude/1.2)*(b-a)
        ozone += max(0, 1-abs(altitude-25)/15)*(b-a)
    return tuple(math.exp(-r*rayleigh-0.0044*mie-o*ozone)
                 for r, o in zip((0.0058, 0.0135, 0.0331), (0.00065, 0.001881, 0.000085)))


def write_csv(name: str, fields: list[str], rows: list[tuple]) -> None:
    with (OUT / name).open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(fields)
        writer.writerows(tuple(round(v, 9) if isinstance(v, float) else v for v in row) for row in rows)


def main() -> None:
    elevations = [0, 0.25, 0.5, 1, 2, 3, 5, 10, 15, 20, 30, 60, 90]
    tau_values = [0.1, 0.2, 0.4]
    zenith = airmass(90)
    write_csv("airmass.csv", ["apparent_elevation_deg", "relative_airmass",
        "T_over_zenith_tau_0_1", "T_over_zenith_tau_0_2", "T_over_zenith_tau_0_4"],
        [(h, airmass(h), *(math.exp(-tau*(airmass(h)-zenith)) for tau in tau_values)) for h in elevations])
    write_csv("refraction.csv", ["geometric_center_elevation_deg", "refraction_arcmin",
        "apparent_center_elevation_deg", "vertical_over_original_diameter"],
        [(h, refraction_deg(h)*60, h+refraction_deg(h), vertical_ratio(h)) for h in [0, 1, 2, 5, 10, 20, 30, 45]])
    write_csv("first-hours.csv", ["latitude_deg", "declination_deg", "minutes_after_conventional_sunrise",
        "geometric_center_elevation_deg"],
        [(38.72, d, t, solar_elevation(t, d)) for d in [-23.44, 0, 23.44] for t in [0, 5, 15, 30, 60, 90, 120]])
    write_csv("apparent-size.csv", ["center_elevation_input_deg", "physical_gain", "current_artistic_gain",
        "artistic_solar_diameter_deg"],
        [(h, 1, artistic_gain(h), 2*SUN_RADIUS_DEG*artistic_gain(h)) for h in [0, 1, 2, 5, 10, 15, 20, 25, 30]])
    write_csv("moon-phase.csv", ["phase_angle_deg", "geometric_illuminated_fraction",
        "relative_flux_without_opposition_or_extinction"],
        [(a, (1+math.cos(math.radians(a)))/2, lunar_phase_flux(a)) for a in [0, 30, 60, 90, 120, 150]])
    write_csv("current-model-transmission.csv", ["geometric_ray_elevation_deg", "linear_T_R", "linear_T_G", "linear_T_B"],
        [(h, *current_rgb_transmission(h)) for h in elevations])

    # Arithmetic anchors and invariants, not claims of observed atmospheric accuracy.
    assert abs(airmass(0)-37.919608) < 1e-5
    assert abs(refraction_deg(0)*60-28.916666667) < 1e-7
    assert 0.85 < vertical_ratio(0) < 0.86
    assert abs(lunar_phase_flux(90)-0.09099635576873659) < 1e-12
    assert artistic_gain(0) == 3.2 and artistic_gain(25) == 1
    assert all(abs(solar_elevation(0, d)+0.8333) < 1e-10 for d in [-23.44, 0, 23.44])
    assert all(0 < x <= 1 for h in elevations for x in current_rgb_transmission(h))

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10, "axes.titlesize": 12,
        "axes.spines.top": False, "axes.spines.right": False, "figure.facecolor": "#fafaf7",
        "axes.facecolor": "#fafaf7", "axes.grid": True, "grid.alpha": 0.2})
    fig, axes = plt.subplots(3, 2, figsize=(13, 12.5), layout="constrained")
    hs = [i/10 for i in range(301)]
    axes[0, 0].plot(hs, [airmass(h) for h in hs], color="#216c86", lw=2)
    axes[0, 0].set(title="1. Mais atmosfera junto ao horizonte", xlabel="Elevação aparente (°)", ylabel="Massa de ar relativa")
    for tau, color in zip(tau_values, ["#216c86", "#c27a19", "#ad4141"]):
        axes[0, 1].semilogy(hs, [math.exp(-tau*(airmass(h)-zenith)) for h in hs], label=f"τ = {tau:g}", color=color, lw=2)
    axes[0, 1].set(title="2. Extinção: três exemplos, não medições", xlabel="Elevação aparente (°)", ylabel="Transmissão / transmissão no zênite")
    axes[0, 1].legend()
    hg = [i/20 for i in range(401)]
    axes[1, 0].plot(hg, [vertical_ratio(h) for h in hg], color="#216c86", lw=2)
    axes[1, 0].axhline(1, ls="--", color="#777777", lw=1)
    axes[1, 0].set(title="3. Disco achatado: aproximação NOAA", xlabel="Elevação geométrica do centro (°)", ylabel="Diâmetro vertical / original", ylim=(0.83, 1.01))
    minutes = list(range(121))
    for d, color in zip([-23.44, 0, 23.44], ["#216c86", "#777777", "#c27a19"]):
        axes[1, 1].plot(minutes, [solar_elevation(t, d) for t in minutes], label=f"Declinação {d:+g}°", color=color, lw=2)
    axes[1, 1].axhline(0, color="#777777", lw=1, ls="--")
    axes[1, 1].set(title="4. Primeiras 2 h: exemplo a 38,72° N", xlabel="Minutos desde centro a −0,8333°", ylabel="Elevação geométrica solar (°)")
    axes[1, 1].legend(fontsize=9)
    axes[2, 0].plot(hs, [artistic_gain(h) for h in hs], label="Ampliação artística atual", color="#c27a19", lw=2)
    axes[2, 0].axhline(1, color="#216c86", lw=2, label="Escala física de referência")
    axes[2, 0].set(title="5. Ampliação visual é uma escolha separada", xlabel="Elevação usada pelo controle (°)", ylabel="Multiplicador do diâmetro", ylim=(0.8, 3.4))
    axes[2, 0].legend(fontsize=9)
    phases = list(range(151))
    axes[2, 1].plot(phases, [100*lunar_phase_flux(a) for a in phases], label="Fluxo: modelo empírico básico", color="#216c86", lw=2)
    axes[2, 1].plot(phases, [50*(1+math.cos(math.radians(a))) for a in phases], label="Área iluminada geométrica", color="#777777", ls="--", lw=2)
    axes[2, 1].set(title="6. Meia Lua não significa metade da luz", xlabel="Ângulo de fase (°): 0 = cheia", ylabel="Percentual da referência (%)")
    axes[2, 1].legend(fontsize=9)
    fig.suptitle("Sol e Lua no oceano • curvas para orientar o próximo refinamento", fontsize=17)
    fig.supxlabel("Estudo de 27/09/2026 • fontes, fórmulas e limites no README • nenhum gráfico representa captura do renderer", fontsize=9)
    fig.savefig(OUT / "celestial-evolution.png", dpi=160)
    plt.close(fig)
    print("6 CSVs + celestial-evolution.png generated; arithmetic checks passed.")
    for d in [-23.44, 0, 23.44]:
        print(f"Solar declination {d:+g}: " + ", ".join(f"{t} min = {solar_elevation(t,d):.2f} deg" for t in [5,15,30,60,120]))
    for h in [0,1,2,5,10,20]:
        print(f"h={h}: air mass={airmass(h):.2f}; T/tau0.2={math.exp(-0.2*(airmass(h)-zenith)):.5f}; current RGB T={current_rgb_transmission(h)}")


if __name__ == "__main__":
    main()
