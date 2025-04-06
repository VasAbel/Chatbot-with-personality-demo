using UnityEngine;

public class NpcMovement : Movement
{
    void Update()
    {
        if (canMove)
        {
            movement = Vector3.left;
        }
        else
        {
            movement = Vector3.zero;
        }
    }

    protected override void FixedUpdate()
    {
        moveCharacter(-movement);
    }
}
