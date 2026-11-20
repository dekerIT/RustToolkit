import unittest

from services.alarm_engine import AlarmEngine


class AlarmEngineTests(unittest.TestCase):
    def setUp(self) -> None:
        self.engine = AlarmEngine()
        self.engine.configure(50, True, False)

    def test_trigger_and_stop_state(self) -> None:
        self.engine.trigger("Test")
        self.assertTrue(self.engine.active)
        self.engine.stop()
        self.assertFalse(self.engine.active)

    def test_stop_when_idle_is_safe(self) -> None:
        self.engine.stop()
        self.assertFalse(self.engine.active)


if __name__ == "__main__":
    unittest.main()
