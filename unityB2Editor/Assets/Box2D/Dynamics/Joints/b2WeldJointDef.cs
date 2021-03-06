﻿/*
* Copyright (c) 2006-2007 Erin Catto http://www.gphysics.com
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/
using Box2D.Common.Math;
using Box2D.Dynamics.Joints;
using Box2D.Dynamics;
using Box2D.Common;

namespace Box2D.Dynamics.Joints{



/**
 * Weld joint definition. You need to specify local anchor points
 * where they are attached and the relative body angle. The position
 * of the anchor points is important for computing the reaction torque.
 * @see b2WeldJoint
 */
public class b2WeldJointDef : b2JointDef
{
	public b2WeldJointDef()
	{
		type = b2Joint.e_weldJoint;
		referenceAngle = 0.0f;
	}
	
	/**
	 * Initialize the bodies, anchors, axis, and reference angle using the world
	 * anchor and world axis.
	 */
	public void Initialize(b2Body bA, b2Body bB,
								b2Vec2 anchor)
	{
		bodyA = bA;
		bodyB = bB;
		localAnchorA.SetV( bodyA.GetLocalPoint(anchor));
		localAnchorB.SetV( bodyB.GetLocalPoint(anchor));
		referenceAngle = bodyB.GetAngle() - bodyA.GetAngle();
	}

	/**
	* The local anchor point relative to bodyA's origin.
	*/
	public b2Vec2 localAnchorA = new b2Vec2();

	/**
	* The local anchor point relative to bodyB's origin.
	*/
	public b2Vec2 localAnchorB = new b2Vec2();

	/**
	 * The body2 angle minus body1 angle in the reference state (radians).
	 */
	public float referenceAngle;
}

}