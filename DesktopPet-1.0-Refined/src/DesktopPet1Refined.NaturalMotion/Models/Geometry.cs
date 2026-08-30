namespace DesktopPet1Refined.NaturalMotion.Models;

public readonly record struct MotionPoint(double X, double Y);

public readonly record struct MotionSize(double Width, double Height);

public readonly record struct MotionRect(double X, double Y, double Width, double Height);
