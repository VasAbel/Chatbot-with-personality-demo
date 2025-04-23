using UnityEngine;

public class NpcMovement : Movement
{
    private Transform destination;
    void Update()
    {
        if (canMove && destination != null)
        {
            Vector3 direction = (destination.position - transform.position).normalized;
            movement = direction;
            float distance = Vector3.Distance(transform.position, destination.position);
                if (distance < 0.2f)
                {
                    movement = Vector3.zero;
                    canMove = false;
                    destination = null;
                }
        }
        else
        {
            movement = Vector3.zero;
        }
    }

    protected override void FixedUpdate()
    {
        moveCharacter(movement);
    }

    public void MoveTo(string placeToGo)
    {
        Transform target = PlaceRegistry.Instance.GetPlaceByName(placeToGo);
        if (target != null)
        {
            destination = target;
            canMove = true;
        }
    }
}
