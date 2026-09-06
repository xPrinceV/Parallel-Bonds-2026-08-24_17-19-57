using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ParallelBonds.BuffChecks
{
    // keep assertions and temporary objects local to one test group
    internal sealed class BuffCheckContext : IDisposable
    {
        private readonly string group;
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public int CheckCount { get; private set; }

        public BuffCheckContext(string group)
        {
            this.group = group;
        }

        public void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("Failed [" + group + "]: " + name);
            CheckCount++;
        }

        public GameObject CreateObject(string name)
        {
            var gameObject = new GameObject("Buff check " + group + " " + name);
            created.Add(gameObject);
            return gameObject;
        }

        public void Set(object target, string field, object value)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            FieldInfo info = target.GetType().GetField(field, PrivateInstance);
            if (info == null)
                throw new MissingFieldException(target.GetType().FullName, field);
            info.SetValue(target, value);
        }

        public void Invoke(object target, string method)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            MethodInfo info = target.GetType().GetMethod(method, PrivateInstance, null, Type.EmptyTypes, null);
            if (info == null)
                throw new MissingMethodException(target.GetType().FullName, method);
            info.Invoke(target, null);
        }

        public BuffDefinition Recipe(params BuffAtom[] atoms)
        {
            var definition = ScriptableObject.CreateInstance<BuffDefinition>();
            created.Add(definition);
            Set(definition, "atoms", new List<BuffAtom>(atoms));
            return definition;
        }

        public BuffDefinition DamageReward()
        {
            var damage = new DamageBuffAtom();
            Set(damage, "damageMultiplier", 1.5f);
            return Recipe(damage);
        }

        public StackToActivationBuffAtom Trigger(BuffDefinition reward, StackConsumeMode mode)
        {
            var atom = new StackToActivationBuffAtom();
            Set(atom, "stackCount", 5);
            Set(atom, "consumeMode", mode);
            var action = new GrantBuffActivation();
            Set(action, "targetBuff", reward);
            Set(atom, "activation", action);
            return atom;
        }

        public void Dispose()
        {
            // destroy holders before definitions that their lifecycle callbacks may access
            foreach (UnityEngine.Object item in created)
                if (item is GameObject && item != null)
                    UnityEngine.Object.DestroyImmediate(item);
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null)
                    UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }
    }

    // simulate grant success or failure without coupling stack tests to the controller
    internal sealed class RecordingBuffReceiver : IBuffReceiver
    {
        public GameObject Owner { get; set; }
        public bool Accept = true;
        public int Grants;

        public bool TryAddBuff(BuffDefinition definition)
        {
            if (!Accept || definition == null || !definition.isValid)
                return false;
            Grants++;
            return true;
        }
    }
}
