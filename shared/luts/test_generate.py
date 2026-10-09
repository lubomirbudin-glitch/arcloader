"""Properties every built-in LUT must have. Run: .venv\\Scripts\\python -m unittest test_generate.py"""
import filecmp
import os
import tempfile
import unittest

import numpy as np

import generate as gen

# Camera code value (code / 1023) of 18 % grey, from the vendors' white papers.
REFERENCE_GREY = {
    "arri-logc3-awg3": 0.391007,
    "arri-logc4-awg4": 0.278396,
    "sony-slog3-sgamut3cine": 0.410557,
    "sony-slog3-sgamut3": 0.410557,
    "red-log3g10-rwg": 0.333333,
    "panasonic-vlog-vgamut": 0.423311,
}


# Camera code value (code / 1023) of scene black (0 % reflectance), from the same white papers.
REFERENCE_BLACK = {
    "arri-logc3-awg3": 0.092809,
    "arri-logc4-awg4": 95 / 1023,
    "sony-slog3-sgamut3cine": 95 / 1023,
    "sony-slog3-sgamut3": 95 / 1023,
    "bmd-film-gen5": 0.092466,
    "red-log3g10-rwg": 0.091551,
    "canon-clog2-cinemagamut": 0.092864,
    "canon-clog3-cinemagamut": 128 / 1023,
    "panasonic-vlog-vgamut": 128 / 1024,
    "dji-dlog-dgamut": 0.0929,
    "fujifilm-flog2-fgamut": 95 / 1023,
}

# The noise floor of current cinema cameras sits about six stops under 18 % grey; from there down the picture must
# look black (ARRI's own Rec.709 LUT puts it near display black too) — not the lifted, washed-out grey of a plain gamma.
NOISE_FLOOR = 0.18 * 2.0 ** -6
BLACK_MAX = 0.03


def grey_input(p, linear=0.18):
    return gen.code_to_video(gen.encode(p, np.array([linear, linear, linear])))


def cube_neutral(p, code):
    """Output (green) of the generated cube for a neutral camera code, interpolated along the grey diagonal like ffmpeg."""
    n = gen.SIZE
    axis = np.linspace(0.0, 1.0, n)
    diag = gen.transform(p, np.stack([axis] * 3, axis=-1))[:, 1]
    return float(np.interp(float(gen.code_to_video(code)), axis, diag))


class Properties(unittest.TestCase):
    def test_grey_is_042_and_neutral(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p))
            self.assertTrue(np.all(np.abs(out - 0.42) <= 0.01), (p["id"], out))
            self.assertLessEqual(out.max() - out.min(), 0.002, p["id"])

    def test_black_stays_black(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, 0.0))
            self.assertLessEqual(out.max(), 0.02, (p["id"], out))

    def test_camera_black_code_is_display_black(self):
        self.assertEqual(set(REFERENCE_BLACK), {p["id"] for p in gen.PROFILES})
        for p in gen.PROFILES:
            code = REFERENCE_BLACK[p["id"]]
            self.assertAlmostEqual(float(gen.encode(p, np.array([0.0]))[0]), code, delta=0.001, msg=p["id"])
            out = gen.transform(p, gen.code_to_video(np.array([code] * 3)))
            self.assertLessEqual(out.max(), BLACK_MAX, (p["id"], out))
            # what ffmpeg actually applies: the 33-point cube, interpolated
            self.assertLessEqual(cube_neutral(p, code), BLACK_MAX, p["id"])

    def test_noise_floor_is_display_black(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, NOISE_FLOOR))
            self.assertLessEqual(out.max(), BLACK_MAX, (p["id"], out))
            self.assertLessEqual(cube_neutral(p, float(gen.encode(p, np.array([NOISE_FLOOR]))[0])), 0.045, p["id"])

    def test_shadows_keep_detail(self):
        # the toe darkens, it does not crush: two stops under grey stays a clearly visible dark grey
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, 0.18 / 4))
            self.assertTrue(np.all((out >= 0.15) & (out <= 0.23)), (p["id"], out))

    def test_neutral_axis_is_neutral_and_monotonic(self):
        for p in gen.PROFILES:
            stops = 0.18 * 2.0 ** np.arange(-8, 6.5, 0.5)
            outs = np.array([gen.transform(p, grey_input(p, s)) for s in stops])
            self.assertTrue(np.all(outs.max(axis=1) - outs.min(axis=1) <= 0.002), p["id"])
            self.assertTrue(np.all(np.diff(outs[:, 1]) >= -1e-9), p["id"])

    def test_six_stops_over_grey_reaches_white(self):
        for p in gen.PROFILES:
            out = gen.transform(p, grey_input(p, 0.18 * 2 ** 6))
            self.assertGreaterEqual(out.min(), 0.98, (p["id"], out))

    def test_whole_cube_in_range(self):
        for p in gen.PROFILES:
            out = gen.transform(p, gen.grid())
            self.assertFalse(np.isnan(out).any(), p["id"])
            self.assertGreaterEqual(out.min(), 0.0, p["id"])
            self.assertLessEqual(out.max(), 1.0, p["id"])

    def test_grey_code_matches_white_papers(self):
        for p in gen.PROFILES:
            if p["id"] in REFERENCE_GREY:
                code = float(gen.encode(p, np.array([0.18]))[0])
                self.assertAlmostEqual(code, REFERENCE_GREY[p["id"]], delta=0.003, msg=p["id"])


class Files(unittest.TestCase):
    def test_deterministic(self):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            gen.write_all(a)
            gen.write_all(b)
            for name in os.listdir(a):
                self.assertTrue(filecmp.cmp(os.path.join(a, name), os.path.join(b, name), shallow=False), name)

    def test_committed_files_match_generator(self):
        with tempfile.TemporaryDirectory() as fresh:
            gen.write_all(fresh)
            for name in os.listdir(fresh):
                committed = os.path.join(gen.HERE, name)
                self.assertTrue(os.path.exists(committed), f"{name} missing — run generate.py")
                self.assertTrue(filecmp.cmp(os.path.join(fresh, name), committed, shallow=False), f"{name} is stale")

    def test_cube_header_and_size(self):
        text = gen.cube_text(gen.PROFILES[0]).splitlines()
        self.assertIn("LUT_3D_SIZE 33", text)
        self.assertEqual(sum(1 for line in text if line and line[0].isdigit()), 33 ** 3)


if __name__ == "__main__":
    unittest.main()
