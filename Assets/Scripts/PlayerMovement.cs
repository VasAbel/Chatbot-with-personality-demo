using UnityEngine;

public class PlayerMovement : Movement
{

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }
    void Update()
    {
        if (canMove)
        {
            movement = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical")).normalized;          
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
