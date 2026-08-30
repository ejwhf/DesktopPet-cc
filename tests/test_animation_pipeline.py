from pathlib import Path
import json
import unittest

from tools.animation_pipeline import CLIPS, EXPECTED_FRAME_COUNT


class AnimationContractTests(unittest.TestCase):
    def test_contract_has_20_clips_and_160_frames(self) -> None:
        self.assertEqual(len(CLIPS), 20)
        self.assertEqual(sum(clip.frame_count for clip in CLIPS), EXPECTED_FRAME_COUNT)
        self.assertEqual(EXPECTED_FRAME_COUNT, 160)
        self.assertEqual(len({clip.clip_id for clip in CLIPS}), len(CLIPS))

    def test_frame_durations_and_fallbacks_are_valid(self) -> None:
        for clip in CLIPS:
            self.assertEqual(len(clip.sources), len(clip.durations_ms), clip.clip_id)
            self.assertTrue(all(duration > 0 for duration in clip.durations_ms), clip.clip_id)
            self.assertTrue(clip.fallback_pose_id)

    def test_checked_in_manifest_matches_contract(self) -> None:
        root = Path(__file__).resolve().parents[1]
        document = json.loads((root / "assets/manifest/animations.json").read_text(encoding="utf-8"))
        self.assertEqual(document["frameCount"], EXPECTED_FRAME_COUNT)
        self.assertEqual(
            {clip["id"]: len(clip["frames"]) for clip in document["clips"]},
            {clip.clip_id: clip.frame_count for clip in CLIPS},
        )


if __name__ == "__main__":
    unittest.main()
