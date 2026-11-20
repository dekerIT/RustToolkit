import json
from pathlib import Path
import unittest


class RaidDataTests(unittest.TestCase):
    def test_raid_data_is_valid_json(self) -> None:
        root = Path(__file__).resolve().parent.parent
        data = json.loads((root / "data" / "raid_data.json").read_text(encoding="utf-8"))

        self.assertEqual(data["metadata"]["status"], "unverified")
        self.assertTrue(data["structures"])
        self.assertTrue(all(structure["verified"] is False for structure in data["structures"]))


if __name__ == "__main__":
    unittest.main()
