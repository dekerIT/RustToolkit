using RustPlusApi.Data;

namespace RustToolkit.Desktop.Models;

public sealed record RustPlusMapSnapshot(ServerMap Map, MapMarkers Markers, double MapSize);
