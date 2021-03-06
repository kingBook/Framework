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
using Box2D.Common;
using Box2D.Dynamics.Contacts;
using Box2D.Dynamics.Joints;
using System.Collections.Generic;

namespace Box2D.Dynamics{


/*
Position Correction Notes
=========================
I tried the several algorithms for position correction of the 2D revolute joint.
I looked at these systems:
- simple pendulum (1m diameter sphere on massless 5m stick) with initial angular velocity of 100 rad/s.
- suspension bridge with 30 1m long planks of length 1m.
- multi-link chain with 30 1m long links.

Here are the algorithms:

Baumgarte - A fraction of the position error is added to the velocity error. There is no
separate position solver.

Pseudo Velocities - After the velocity solver and position integration,
the position error, Jacobian, and effective mass are recomputed. Then
the velocity constraints are solved with pseudo velocities and a fraction
of the position error is added to the pseudo velocity error. The pseudo
velocities are initialized to zero and there is no warm-starting. After
the position solver, the pseudo velocities are added to the positions.
This is also called the First Order World method or the Position LCP method.

Modified Nonlinear Gauss-Seidel (NGS) - Like Pseudo Velocities except the
position error is re-computed for each constraint and the positions are updated
after the constraint is solved. The radius vectors (aka Jacobians) are
re-computed too (otherwise the algorithm has horrible instability). The pseudo
velocity states are not needed because they are effectively zero at the beginning
of each iteration. Since we have the current position error, we allow the
iterations to terminate early if the error becomes smaller than b2_linearSlop.

Full NGS or just NGS - Like Modified NGS except the effective mass are re-computed
each time a constraint is solved.

Here are the results:
Baumgarte - this is the cheapest algorithm but it has some stability problems,
especially with the bridge. The chain links separate easily close to the root
and they jitter as they struggle to pull together. This is one of the most common
methods in the field. The big drawback is that the position correction artificially
affects the momentum, thus leading to instabilities and false bounce. I used a
bias factor of 0.2. A larger bias factor makes the bridge less stable, a smaller
factor makes joints and contacts more spongy.

Pseudo Velocities - the is more stable than the Baumgarte method. The bridge is
stable. However, joints still separate with large angular velocities. Drag the
simple pendulum in a circle quickly and the joint will separate. The chain separates
easily and does not recover. I used a bias factor of 0.2. A larger value lead to
the bridge collapsing when a heavy cube drops on it.

Modified NGS - this algorithm is better in some ways than Baumgarte and Pseudo
Velocities, but in other ways it is worse. The bridge and chain are much more
stable, but the simple pendulum goes unstable at high angular velocities.

Full NGS - stable in all tests. The joints display good stiffness. The bridge
still sags, but this is better than infinite forces.

Recommendations
Pseudo Velocities are not really worthwhile because the bridge and chain cannot
recover from joint separation. In other cases the benefit over Baumgarte is small.

Modified NGS is not a robust method for the revolute joint due to the violent
instability seen in the simple pendulum. Perhaps it is viable with other constraint
types, especially scalar constraints where the effective mass is a scalar.

This leaves Baumgarte and Full NGS. Baumgarte has small, but manageable instabilities
and is very fast. I don't think we can escape Baumgarte, especially in highly
demanding cases where high constraint fidelity is not needed.

Full NGS is robust and easy on the eyes. I recommend this as an option for
higher fidelity simulation and certainly for suspension bridges and long chains.
Full NGS might be a good choice for ragdolls, especially motorized ragdolls where
joint separation can be problematic. The number of NGS iterations can be reduced
for better performance without harming robustness much.

Each joint in a can be handled differently in the position solver. So I recommend
a system where the user can select the algorithm on a per joint basis. I would
probably default to the slower Full NGS and let the user select the faster
Baumgarte method in performance critical scenarios.
*/


/**
* @private
*/
public class b2Island
{
	
	public b2Island()
	{
		m_bodies = new List<b2Body>();
		m_contacts = new List<b2Contact>();
		m_joints = new List<b2Joint>();
	}
	
	public void Initialize(
			int bodyCapacity,
			int contactCapacity,
			int jointCapacity,
			object allocator,
			b2ContactListener listener,
			b2ContactSolver contactSolver)
	{
		int i;
		
		m_bodyCapacity = bodyCapacity;
		m_contactCapacity = contactCapacity;
		m_jointCapacity	 = jointCapacity;
		m_bodyCount = 0;
		m_contactCount = 0;
		m_jointCount = 0;
		
		m_allocator = allocator;
		m_listener = listener;
		m_contactSolver = contactSolver;
		
		for (i = m_bodies.Count; i < bodyCapacity; i++)
			m_bodies.Add(null);
		
		for (i = m_contacts.Count; i < contactCapacity; i++)
			m_contacts.Add(null);
		
		for (i = m_joints.Count; i < jointCapacity; i++)
			m_joints.Add(null);
		
	}
	//~b2Island();
	
	public void Clear()
	{
		m_bodyCount = 0;
		m_contactCount = 0;
		m_jointCount = 0;
	}

	public void Solve(b2TimeStep step, b2Vec2 gravity, bool allowSleep)
	{
		int i;
		int j;
		b2Body b;
		b2Joint joint;
		
		// Integrate velocities and apply damping.
		for (i = 0; i < m_bodyCount; ++i)
		{
			b = m_bodies[i];
			
			if (b.GetType() != b2Body.b2_dynamicBody)
				continue;
			//=====================add by kingBook 2015/10/26 17:25==============
			b2Vec2 l_gravity;
			if(b.m_customGravity!=null)l_gravity=b.m_customGravity;
			else l_gravity=gravity;
			//==============================added================================
			// Integrate velocities.
			//b.m_linearVelocity += step.dt * (gravity + b.m_invMass * b.m_force);
			b.m_linearVelocity.x += step.dt * (l_gravity.x + b.m_invMass * b.m_force.x);
			b.m_linearVelocity.y += step.dt * (l_gravity.y + b.m_invMass * b.m_force.y);
			b.m_angularVelocity += step.dt * b.m_invI * b.m_torque;
			
			// Apply damping.
			// ODE: dv/dt + c * v = 0
			// Solution: v(t) = v0 * exp(-c * t)
			// Time step: v(t + dt) = v0 * exp(-c * (t + dt)) = v0 * exp(-c * t) * exp(-c * dt) = v * exp(-c * dt)
			// v2 = exp(-c * dt) * v1
			// Taylor expansion:
			// v2 = (1.0f - c * dt) * v1
			b.m_linearVelocity.Multiply( b2Math.Clamp(1.0f - step.dt * b.m_linearDamping, 0.0f, 1.0f) );
			b.m_angularVelocity *= b2Math.Clamp(1.0f - step.dt * b.m_angularDamping, 0.0f, 1.0f);
		}
		
		m_contactSolver.Initialize(step, m_contacts, m_contactCount, m_allocator);
		b2ContactSolver contactSolver = m_contactSolver;

		// Initialize velocity constraints.
		contactSolver.InitVelocityConstraints(step);
		
		for (i = 0; i < m_jointCount; ++i)
		{
			joint = m_joints[i];
			joint.InitVelocityConstraints(step);
		}
		
		// Solve velocity constraints.
		for (i = 0; i < step.velocityIterations; ++i)
		{	
			for (j = 0; j < m_jointCount; ++j)
			{
				joint = m_joints[j];
				joint.SolveVelocityConstraints(step);
			}
			
			contactSolver.SolveVelocityConstraints();
		}
		
		// Post-solve (store impulses for warm starting).
		for (i = 0; i < m_jointCount; ++i)
		{
			joint = m_joints[i];
			joint.FinalizeVelocityConstraints();
		}
		contactSolver.FinalizeVelocityConstraints();
		
		// Integrate positions.
		for (i = 0; i < m_bodyCount; ++i)
		{
			b = m_bodies[i];
			
			if (b.GetType() == b2Body.b2_staticBody)
				continue;
				
			// Check for large velocities.
			// b2Vec2 translation = step.dt * b.m_linearVelocity;
			float translationX = step.dt * b.m_linearVelocity.x;
			float translationY = step.dt * b.m_linearVelocity.y;
			//if (b2Dot(translation, translation) > b2_maxTranslationSquared)
			if ((translationX*translationX+translationY*translationY) > b2Settings.b2_maxTranslationSquared)
			{
				b.m_linearVelocity.Normalize();
				b.m_linearVelocity.x *= b2Settings.b2_maxTranslation * step.inv_dt;
				b.m_linearVelocity.y *= b2Settings.b2_maxTranslation * step.inv_dt;
			}
			float rotation = step.dt * b.m_angularVelocity;
			if (rotation * rotation > b2Settings.b2_maxRotationSquared)
			{
				if (b.m_angularVelocity < 0.0f)
				{
					b.m_angularVelocity = -b2Settings.b2_maxRotation * step.inv_dt;
				}
				else
				{
					b.m_angularVelocity = b2Settings.b2_maxRotation * step.inv_dt;
				}
			}
			
			// Store positions for continuous collision.
			b.m_sweep.c0.SetV(b.m_sweep.c);
			b.m_sweep.a0 = b.m_sweep.a;
			
			// Integrate
			//b.m_sweep.c += step.dt * b.m_linearVelocity;
			
			//--------------修改start kingBook---------------
			if(b.m_allowMovement){
				b.m_sweep.c.x += step.dt * b.m_linearVelocity.x;
				b.m_sweep.c.y += step.dt * b.m_linearVelocity.y;
			}
			//--------------修改 end-------------------------
			
			b.m_sweep.a += step.dt * b.m_angularVelocity;
			// Compute new transform
			b.SynchronizeTransform();
			
			// Note: shapes are synchronized later.
		}
		
		// Iterate over constraints.
		for (i = 0; i < step.positionIterations; ++i)
		{
			bool contactsOkay = contactSolver.SolvePositionConstraints(b2Settings.b2_contactBaumgarte);
			
			bool jointsOkay = true;
			for (j = 0; j < m_jointCount; ++j)
			{
				joint = m_joints[j];
				bool jointOkay = joint.SolvePositionConstraints(b2Settings.b2_contactBaumgarte);
				jointsOkay = jointsOkay && jointOkay;
			}
			
			if (contactsOkay && jointsOkay)
			{
				break;
			}
		}
		
		Report(contactSolver.m_constraints);
		
		if (allowSleep){
			
			float minSleepTime = float.MaxValue;
			
			float linTolSqr = b2Settings.b2_linearSleepTolerance * b2Settings.b2_linearSleepTolerance;
			float angTolSqr = b2Settings.b2_angularSleepTolerance * b2Settings.b2_angularSleepTolerance;
			
			for (i = 0; i < m_bodyCount; ++i)
			{
				b = m_bodies[i];
				if (b.GetType() == b2Body.b2_staticBody)
				{
					continue;
				}
				
				if ((b.m_flags & b2Body.e_allowSleepFlag) == 0)
				{
					b.m_sleepTime = 0.0f;
					minSleepTime = 0.0f;
				}
				
				if ((b.m_flags & b2Body.e_allowSleepFlag) == 0 ||
					b.m_angularVelocity * b.m_angularVelocity > angTolSqr ||
					b2Math.Dot(b.m_linearVelocity, b.m_linearVelocity) > linTolSqr)
				{
					b.m_sleepTime = 0.0f;
					minSleepTime = 0.0f;
				}
				else
				{
					b.m_sleepTime += step.dt;
					minSleepTime = b2Math.Min(minSleepTime, b.m_sleepTime);
				}
			}
			
			if (minSleepTime >= b2Settings.b2_timeToSleep)
			{
				for (i = 0; i < m_bodyCount; ++i)
				{
					b = m_bodies[i]; 
					b.SetAwake(false);
				}
			}
		}
	}
	
	
	public void SolveTOI(b2TimeStep subStep)
	{
		int i;
		int j;
		m_contactSolver.Initialize(subStep, m_contacts, m_contactCount, m_allocator);
		b2ContactSolver contactSolver = m_contactSolver;
		
		// No warm starting is needed for TOI events because warm
		// starting impulses were applied in the discrete solver.

		// Warm starting for joints is off for now, but we need to
		// call this function to compute Jacobians.
		for (i = 0; i < m_jointCount;++i)
		{
			m_joints[i].InitVelocityConstraints(subStep);
		}
		
		
		// Solve velocity constraints.
		for (i = 0; i < subStep.velocityIterations; ++i)
		{
			contactSolver.SolveVelocityConstraints();
			for (j = 0; j < m_jointCount;++j)
			{
				m_joints[j].SolveVelocityConstraints(subStep);
			}
		}
		
		// Don't store the TOI contact forces for warm starting
		// because they can be quite large.
		
		// Integrate positions.
		for (i = 0; i < m_bodyCount; ++i)
		{
			b2Body b = m_bodies[i];
			
			if (b.GetType() == b2Body.b2_staticBody)
				continue;
				
			// Check for large velocities.
			// b2Vec2 translation = subStep.dt * b.m_linearVelocity;
			float translationX = subStep.dt * b.m_linearVelocity.x;
			float translationY = subStep.dt * b.m_linearVelocity.y;
			//if (b2Dot(translation, translation) > b2_maxTranslationSquared)
			if ((translationX*translationX+translationY*translationY) > b2Settings.b2_maxTranslationSquared)
			{
				b.m_linearVelocity.Normalize();
				b.m_linearVelocity.x *= b2Settings.b2_maxTranslation * subStep.inv_dt;
				b.m_linearVelocity.y *= b2Settings.b2_maxTranslation * subStep.inv_dt;
			}
			
			float rotation = subStep.dt * b.m_angularVelocity;
			if (rotation * rotation > b2Settings.b2_maxRotationSquared)
			{
				if (b.m_angularVelocity < 0.0f)
				{
					b.m_angularVelocity = -b2Settings.b2_maxRotation * subStep.inv_dt;
				}
				else
				{
					b.m_angularVelocity = b2Settings.b2_maxRotation * subStep.inv_dt;
				}
			}
			
			// Store positions for continuous collision.
			b.m_sweep.c0.SetV(b.m_sweep.c);
			b.m_sweep.a0 = b.m_sweep.a;
			
			// Integrate
			b.m_sweep.c.x += subStep.dt * b.m_linearVelocity.x;
			b.m_sweep.c.y += subStep.dt * b.m_linearVelocity.y;
			b.m_sweep.a   += subStep.dt * b.m_angularVelocity;
			
			// Compute new transform
			b.SynchronizeTransform();
			
			// Note: shapes are synchronized later.
		}
		
		// Solve position constraints.
		float k_toiBaumgarte = 0.75f;
		for (i = 0; i < subStep.positionIterations; ++i)
		{
			bool contactsOkay = contactSolver.SolvePositionConstraints(k_toiBaumgarte);
			bool jointsOkay = true;
			for (j = 0; j < m_jointCount;++j)
			{
				bool jointOkay = m_joints[j].SolvePositionConstraints(b2Settings.b2_contactBaumgarte);
				jointsOkay = jointsOkay && jointOkay;
			}
			
			if (contactsOkay && jointsOkay)
			{
				break;
			}
		}
		Report(contactSolver.m_constraints);
	}

	private static b2ContactImpulse s_impulse = new b2ContactImpulse();
	public void Report(List<b2ContactConstraint> constraints)
	{
		if (m_listener == null)
		{
			return;
		}
		
		for (int i = 0; i < m_contactCount; ++i)
		{
			b2Contact c = m_contacts[i];
			b2ContactConstraint cc = constraints[ i ];
			
			for (int j = 0; j < cc.pointCount; ++j)
			{
				s_impulse.normalImpulses[j] = cc.points[j].normalImpulse;
				s_impulse.tangentImpulses[j] = cc.points[j].tangentImpulse;
			}
			m_listener.PostSolve(c, s_impulse);
		}
	}
	

	public void AddBody(b2Body body)
	{
		//b2Settings.b2Assert(m_bodyCount < m_bodyCapacity);
		body.m_islandIndex = m_bodyCount;
		m_bodies[m_bodyCount++] = body;
	}

	public void AddContact(b2Contact contact)
	{
		//b2Settings.b2Assert(m_contactCount < m_contactCapacity);
		m_contacts[m_contactCount++] = contact;
	}

	public void AddJoint(b2Joint joint)
	{
		//b2Settings.b2Assert(m_jointCount < m_jointCapacity);
		m_joints[m_jointCount++] = joint;
	}

	private object m_allocator;
	private b2ContactListener m_listener;
	private b2ContactSolver m_contactSolver;

	public List<b2Body> m_bodies;
	public List<b2Contact> m_contacts;
	public List<b2Joint> m_joints;

	public int m_bodyCount;
	public int m_jointCount;
	public int m_contactCount;

	private int m_bodyCapacity;
	public int m_contactCapacity;
	public int m_jointCapacity;
	
}

}
