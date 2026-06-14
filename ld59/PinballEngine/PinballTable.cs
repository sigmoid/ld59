using System.Collections.Generic;

public class PinballTable
{
    public List<PinballObstacle> Obstacles;
    private List<PinballBall> _balls = new();
    public float DefaultAcceleration      { get; set; }
    public float Damping                  { get; set; }
    public float RestingVelocityThreshold { get; set; } = 50f;

    public void Update(float deltaTime)
    {
        foreach(var obstacle in Obstacles)
        {
            obstacle.Update(deltaTime);
        }

        foreach(var ball in Balls)
        {
            ball.Update(deltaTime);
        }
    }   

    public List<PinballBall> Balls
    {
        get => _balls;
    }

    public void AddBall(PinballBall ball)
    {
        _balls.Add(ball);
    }
}