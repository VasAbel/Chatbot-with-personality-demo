using UnityEngine;

public class Npc : MonoBehaviour
{
    public float speed = 0.000001f;
    private Rigidbody rb;
    public Vector3 movement;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        movement = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical")).normalized;
    }

    void FixedUpdate()
    {
        moveCharacter(-movement);
    }


    void moveCharacter(Vector3 direction)
    {
        rb.velocity = direction * speed;
    }
}
