﻿// Copyright (c) 2016 Nora
// Released under the MIT license
// http://opensource.org/licenses/mit-license.phpusing

//#define _ENABLE_LIMBIK_FORCEFIX

using UnityEngine;

namespace SA
{
	public partial class FullBodyIK : MonoBehaviour
	{
		public class LimbIK
		{
			struct TwistBone
			{
				public Bone bone;
				public float rate;
			}

#if SAFULLBODYIK_DEBUG
			DebugData _debugData;
#endif

			Settings _settings;
			Settings.LimbIK _settingsLimbIK;
			InternalValues _internalValues;
			InternalValues.LimbIK _internalValuesLimbIK;

			public LimbIKLocation _limbIKLocation;
			LimbIKType _limbIKType;
			Side _limbIKSide;

			Bone _beginBone;
			Bone _bendingBone;
			Bone _endBone;
			Effector _bendingEffector;
			Effector _endEffector;

			TwistBone[] _armTwistBones;
			TwistBone[] _handTwistBones;

			float _beginToBendingLength;
			float _beginToBendingLengthSq;
			float _bendingToEndLength;
			float _bendingToEndLengthSq;
			float _beginToEndLength;
			float _beginToEndLengthSq;
			Matrix3x3 _solvedToBeginBoneBasis = Matrix3x3.identity;
			Matrix3x3 _beginBoneToSolvedBasis = Matrix3x3.identity;
			Matrix3x3 _solvedToBendingBoneBasis = Matrix3x3.identity;

			Matrix3x3 _beginToBendingBoneBasis = Matrix3x3.identity;
			Quaternion _endEffectorToWorldRotation = Quaternion.identity;

			Matrix3x3 _effectorToBeginBoneBasis = Matrix3x3.identity;
			float _defaultSinTheta = 0.0f;
			float _defaultCosTheta = 1.0f;

			float _beginToEndMaxLength = 0.0f;
			CachedScaledValue _effectorMaxLength = CachedScaledValue.zero;
			CachedScaledValue _effectorMinLength = CachedScaledValue.zero;

			float _leg_upperLimitNearCircleZ = 0.0f;
			float _leg_upperLimitNearCircleY = 0.0f;

			CachedScaledValue _arm_elbowBasisForcefixEffectorLengthBegin = CachedScaledValue.zero;
			CachedScaledValue _arm_elbowBasisForcefixEffectorLengthEnd = CachedScaledValue.zero;

			// for Arm twist.
			Matrix3x3 _arm_bendingToBeginBoneBasis = Matrix3x3.identity;
			Quaternion _arm_bendingWorldToBeginBoneRotation = Quaternion.identity;
			// for Hand twist.
			Quaternion _arm_endWorldToBendingBoneRotation = Quaternion.identity;
			// for Arm/Hand twist.(Temporary)
			bool _arm_isSolvedLimbIK;
			Matrix3x3 _arm_solvedBeginBoneBasis = Matrix3x3.identity;
			Matrix3x3 _arm_solvedBendingBoneBasis = Matrix3x3.identity;

			public LimbIK( FullBodyIK fullBodyIK, LimbIKLocation limbIKLocation )
			{
				Assert( fullBodyIK != null );
				if( fullBodyIK == null ) {
					return;
				}

#if SAFULLBODYIK_DEBUG
				_debugData = fullBodyIK._debugData;
#endif

				_settings = fullBodyIK._settings;
				_settingsLimbIK = _settings.limbIK;
				_internalValues = fullBodyIK._internalValues;
				_internalValuesLimbIK = _internalValues.limbIK;

				_limbIKLocation = limbIKLocation;
				_limbIKType = ToLimbIKType( limbIKLocation );
				_limbIKSide = ToLimbIKSide( limbIKLocation );

				if( _limbIKType == LimbIKType.Leg ) {
					var legBones = (_limbIKSide == Side.Left) ? fullBodyIK._leftLegBones : fullBodyIK._rightLegBones;
					var legEffectors = (_limbIKSide == Side.Left) ? fullBodyIK._leftLegEffectors : fullBodyIK._rightLegEffectors;
					_beginBone = legBones.leg;
					_bendingBone = legBones.knee;
					_endBone = legBones.foot;
					_bendingEffector = legEffectors.knee;
					_endEffector = legEffectors.foot;
				} else if( _limbIKType == LimbIKType.Arm ) {
					var armBones = (_limbIKSide == Side.Left) ? fullBodyIK._leftArmBones : fullBodyIK._rightArmBones;
					var armEffectors = (_limbIKSide == Side.Left) ? fullBodyIK._leftArmEffectors : fullBodyIK._rightArmEffectors;
					_beginBone = armBones.arm;
					_bendingBone = armBones.elbow;
					_endBone = armBones.wrist;
					_bendingEffector = armEffectors.elbow;
					_endEffector = armEffectors.wrist;
					_PrepareTwistBones( ref _armTwistBones, armBones.armTwist );
					_PrepareTwistBones( ref _handTwistBones, armBones.handTwist );
				}

				_Prepare( fullBodyIK );
			}

			void _Prepare( FullBodyIK fullBodyIK )
			{
				_beginToBendingLengthSq = (_bendingBone._defaultPosition - _beginBone._defaultPosition).sqrMagnitude;
				_beginToBendingLength = Sqrt( _beginToBendingLengthSq );
				_bendingToEndLengthSq = (_endBone._defaultPosition - _bendingBone._defaultPosition).sqrMagnitude;
				_bendingToEndLength = Sqrt( _bendingToEndLengthSq );
				_beginToEndLengthSq = (_endBone._defaultPosition - _beginBone._defaultPosition).sqrMagnitude;
				_beginToEndLength = Sqrt( _beginToEndLengthSq );

				Vector3 beginToEndDir = _endBone._defaultPosition - _beginBone._defaultPosition;
				if( _SafeNormalize( ref beginToEndDir ) ) {
					if( _limbIKType == LimbIKType.Arm ) {
						if( _limbIKSide == Side.Left ) {
							beginToEndDir = -beginToEndDir;
						}
						Vector3 dirY = _internalValues.defaultRootBasis.column1;
						Vector3 dirZ = _internalValues.defaultRootBasis.column2;
						if( _ComputeBasisLockX( out _effectorToBeginBoneBasis, ref beginToEndDir, ref dirY, ref dirZ ) ) {
                            _effectorToBeginBoneBasis = _effectorToBeginBoneBasis.transpose;
						}
					} else {
						beginToEndDir = -beginToEndDir;
						Vector3 dirX = _internalValues.defaultRootBasis.column0;
						Vector3 dirZ = _internalValues.defaultRootBasis.column2;
						// beginToEffectorBasis( identity to effectorDir(y) )
						if( _ComputeBasisLockY( out _effectorToBeginBoneBasis, ref dirX, ref beginToEndDir, ref dirZ ) ) {
							// effectorToBeginBasis( effectorDir(y) to identity )
							_effectorToBeginBoneBasis = _effectorToBeginBoneBasis.transpose;
						}
					}

					// effectorToBeginBasis( effectorDir(y) to _beginBone._localAxisBasis )
					_effectorToBeginBoneBasis *= _beginBone._localAxisBasis;
				}

				_defaultCosTheta = ComputeCosTheta(
					_bendingToEndLengthSq,			// lenASq
					_beginToEndLengthSq,			// lenBSq
					_beginToBendingLengthSq,		// lenCSq
					_beginToEndLength,				// lenB
					_beginToBendingLength );		// lenC

				_defaultSinTheta = Sqrt( Mathf.Clamp01( 1.0f - _defaultCosTheta * _defaultCosTheta ) );
				CheckNaN( _defaultSinTheta );

				_beginBoneToSolvedBasis = _beginBone._localAxisBasis;
				_solvedToBeginBoneBasis = _beginBone._localAxisBasisInv;
				_solvedToBendingBoneBasis = _bendingBone._localAxisBasisInv;

				_beginToEndMaxLength = _beginToBendingLength + _bendingToEndLength;
				_endEffectorToWorldRotation = Inverse( _endEffector.defaultRotation ) * _endBone._defaultRotation;

				_beginToBendingBoneBasis = _beginBone._localAxisBasisInv * _bendingBone._localAxisBasis;

				if( _limbIKType == LimbIKType.Leg ) {
					_leg_upperLimitNearCircleZ = 0.0f;
					_leg_upperLimitNearCircleY = _beginToEndMaxLength;
				}

				if( _armTwistBones != null ) {
					if( _beginBone != null && _bendingBone != null ) {
						_arm_bendingToBeginBoneBasis = _bendingBone._boneToBaseBasis * _beginBone._baseToBoneBasis;
						_arm_bendingWorldToBeginBoneRotation = Normalize( _bendingBone._worldToBaseBasis * _beginBone._baseToBoneBasis );
                    }
				}

				if( _handTwistBones != null ) {
					if( _endBone != null && _bendingBone != null ) {
						_arm_endWorldToBendingBoneRotation = Normalize( _endBone._worldToBaseBasis * _bendingBone._baseToBoneBasis );
                    }
				}
			}

			float _cache_legUpperLimitAngle = 0.0f;
			float _cache_kneeUpperLimitAngle = 0.0f;

			void _UpdateArgs()
			{
				if( _limbIKType == LimbIKType.Leg ) {
					float effectorMinLengthRate = _settingsLimbIK.legEffectorMinLengthRate;
                    if( _effectorMinLength._b != effectorMinLengthRate ) {
						_effectorMinLength._Reset( _beginToEndMaxLength, effectorMinLengthRate );
					}

					if( _cache_kneeUpperLimitAngle != _settingsLimbIK.prefixKneeUpperLimitAngle ||
						_cache_legUpperLimitAngle != _settingsLimbIK.prefixLegUpperLimitAngle ) {
						_cache_kneeUpperLimitAngle = _settingsLimbIK.prefixKneeUpperLimitAngle;
						_cache_legUpperLimitAngle = _settingsLimbIK.prefixLegUpperLimitAngle;

						// Memo: Their CachedDegreesToCosSin aren't required caching. (Use instantly.)
						CachedDegreesToCosSin kneeUpperLimitTheta = new CachedDegreesToCosSin( _settingsLimbIK.prefixKneeUpperLimitAngle );
						CachedDegreesToCosSin legUpperLimitTheta = new CachedDegreesToCosSin( _settingsLimbIK.prefixLegUpperLimitAngle );

						_leg_upperLimitNearCircleZ = _beginToBendingLength * legUpperLimitTheta.cos
													+ _bendingToEndLength * kneeUpperLimitTheta.cos;

						_leg_upperLimitNearCircleY = _beginToBendingLength * legUpperLimitTheta.sin
													+ _bendingToEndLength * kneeUpperLimitTheta.sin;
					}
				}

				if( _limbIKType == LimbIKType.Arm ) {
					float beginRate = _settingsLimbIK.armBasisForcefixEffectorLengthRate - _settingsLimbIK.armBasisForcefixEffectorLengthLerpRate;
					float endRate = _settingsLimbIK.armBasisForcefixEffectorLengthRate;
					if( _arm_elbowBasisForcefixEffectorLengthBegin._b != beginRate ) {
						_arm_elbowBasisForcefixEffectorLengthBegin._Reset( _beginToEndMaxLength, beginRate );
                    }
					if( _arm_elbowBasisForcefixEffectorLengthEnd._b != endRate ) {
						_arm_elbowBasisForcefixEffectorLengthEnd._Reset( _beginToEndMaxLength, endRate );
					}
				}

				float effectorMaxLengthRate = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.legEffectorMaxLengthRate : _settingsLimbIK.armEffectorMaxLengthRate;
				if( _effectorMaxLength._b != effectorMaxLengthRate ) {
					_effectorMaxLength._Reset( _beginToEndMaxLength, effectorMaxLengthRate );
				}
			}

			// for animatorEnabled
			bool _isPresolvedBending = false;
			Matrix3x3 _presolvedBendingBasis = Matrix3x3.identity;
			Vector3 _presolvedEffectorDir = Vector3.zero;
			float _presolvedEffectorLength = 0.0f;

			// effectorDir to beginBoneBasis
			void _SolveBaseBasis( out Matrix3x3 baseBasis, ref Matrix3x3 parentBaseBasis, ref Vector3 effectorDir )
			{
				if( _limbIKType == LimbIKType.Arm ) {
					Vector3 dirX = (_limbIKSide == Side.Left) ? -effectorDir : effectorDir;
					Vector3 basisY = parentBaseBasis.column1;
					Vector3 basisZ = parentBaseBasis.column2;
					if( _ComputeBasisLockX( out baseBasis, ref dirX, ref basisY, ref basisZ ) ) {
						baseBasis *= _effectorToBeginBoneBasis;
					} else { // Failsafe.(Counts as default effectorDir.)
						baseBasis = parentBaseBasis * _beginBone._localAxisBasis;
					}
				} else {
					Vector3 dirY = -effectorDir;
					Vector3 basisX = parentBaseBasis.column0;
					Vector3 basisZ = parentBaseBasis.column2;
					if( _ComputeBasisLockY( out baseBasis, ref basisX, ref dirY, ref basisZ ) ) {
						baseBasis *= _effectorToBeginBoneBasis;
					} else { // Failsafe.(Counts as default effectorDir.)
						baseBasis = parentBaseBasis * _beginBone._localAxisBasis;
					}
				}
			}

			static void _PrepareTwistBones( ref TwistBone[] twistBones, Bone[] bones )
			{
				if( bones != null && bones.Length > 0 ) {
					int length = bones.Length;
					float t = 1.0f / (float)(length + 1);
					float r = t;
					twistBones = new TwistBone[length];
					for( int i = 0; i < length; ++i, r += t ) {
						twistBones[i].bone = bones[i];
						twistBones[i].rate = r;
					}
				} else {
					twistBones = null;
                }
			}

			public void PresolveBeinding()
			{
				bool presolvedEnabled = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.presolveKneeEnabled : _settingsLimbIK.presolveElbowEnabled;
				if( !presolvedEnabled ) {
					return;
				}

				_isPresolvedBending = false;

				if( _beginBone == null ||
					!_beginBone.transformIsAlive ||
					_beginBone.parentBone == null ||
					!_beginBone.parentBone.transformIsAlive ||
					_bendingEffector == null ||
					_bendingEffector.bone == null ||
					!_bendingEffector.bone.transformIsAlive ||
					_endEffector == null ||
					_endEffector.bone == null ||
					!_endEffector.bone.transformIsAlive ) {
					return ; // Failsafe.
				}

				if( !_internalValues.animatorEnabled ) {
					return; // No require.
				}

				if( _bendingEffector.positionEnabled ) {
					return; // No require.
				}

				if( _limbIKType == LimbIKType.Leg ) {
					if( _settings.limbIK.presolveKneeRate < IKEpsilon ) {
						return; // No effect.
					}
				} else {
					if( _settings.limbIK.presolveElbowRate < IKEpsilon ) {
						return; // No effect.
					}
				}

				Vector3 beginPos = _beginBone.worldPosition;
				Vector3 bendingPos = _bendingEffector.bone.worldPosition;
				Vector3 effectorPos = _endEffector.bone.worldPosition;
				Vector3 effectorTrans = effectorPos - beginPos;
				Vector3 bendingTrans = bendingPos - beginPos;

				float effectorLen = effectorTrans.magnitude;
				float bendingLen = bendingTrans.magnitude;
				if( effectorLen <= IKEpsilon || bendingLen <= IKEpsilon ) {
					return;
				}

				Vector3 effectorDir = effectorTrans * (1.0f / effectorLen);
				Vector3 bendingDir = bendingTrans * (1.0f / bendingLen);

				Matrix3x3 parentBaseBasis = _beginBone.parentBone.worldRotation * _beginBone.parentBone._worldToBaseRotation;
				// Solve EffectorDir Based Basis.
				Matrix3x3 baseBasis;
				_SolveBaseBasis( out baseBasis, ref parentBaseBasis, ref effectorDir );

				_presolvedEffectorDir = effectorDir;
				_presolvedEffectorLength = effectorLen;

				Matrix3x3 toBasis;
				if( _limbIKType == LimbIKType.Arm ) {
					Vector3 dirX = (_limbIKSide == Side.Left) ? -bendingDir : bendingDir;
					Vector3 basisY = parentBaseBasis.column1;
					Vector3 basisZ = parentBaseBasis.column2;
					if( _ComputeBasisLockX( out toBasis, ref dirX, ref basisY, ref basisZ ) ) {
						_presolvedBendingBasis = toBasis * baseBasis.transpose;
						_isPresolvedBending = true;
					}
				} else {
					Vector3 dirY = -bendingDir;
					Vector3 basisX = parentBaseBasis.column0;
					Vector3 basisZ = parentBaseBasis.column2;
					if( _ComputeBasisLockY( out toBasis, ref basisX, ref dirY, ref basisZ ) ) {
						_presolvedBendingBasis = toBasis * baseBasis.transpose;
						_isPresolvedBending = true;
					}
				}
			}

			//------------------------------------------------------------------------------------------------------------

			bool _PrefixLegEffectorPos_UpperNear( ref Vector3 localEffectorTrans )
			{
				float y = localEffectorTrans.y - _leg_upperLimitNearCircleY;
				float z = localEffectorTrans.z;

				float rZ = _leg_upperLimitNearCircleZ;
                float rY = _leg_upperLimitNearCircleY + _effectorMinLength.value;

				if( rZ > IKEpsilon && rY > IKEpsilon ) {
					bool isLimited = false;

					z /= rZ;
					if( y > _leg_upperLimitNearCircleY ) {
						isLimited = true;
					} else {
						y /= rY;
						float len = Sqrt( y * y + z * z );
						if( len < 1.0f ) {
							isLimited = true;
						}
					}

					if( isLimited ) {
						float n = Sqrt( 1.0f - z * z );
						if( n > IKEpsilon ) { // Memo: Upper only.
							localEffectorTrans.y = -n * rY + _leg_upperLimitNearCircleY;
						} else { // Failsafe.
							localEffectorTrans.z = 0.0f;
							localEffectorTrans.y = -_effectorMinLength.value;
						}
						return true;
					}
				}

				return false;
			}

			static bool _PrefixLegEffectorPos_Circular_Far( ref Vector3 localEffectorTrans, float effectorLength )
			{
				return _PrefixLegEffectorPos_Circular( ref localEffectorTrans, effectorLength, true );
            }

			static bool _PrefixLegEffectorPos_Circular( ref Vector3 localEffectorTrans, float effectorLength, bool isFar )
			{
				float y = localEffectorTrans.y;
				float z = localEffectorTrans.z;
				float len = Sqrt( y * y + z * z );
				if( (isFar && len > effectorLength) || (!isFar && len < effectorLength) ) {
					float n = Sqrt( effectorLength * effectorLength - localEffectorTrans.z * localEffectorTrans.z );
					if( n > IKEpsilon ) { // Memo: Lower only.
						localEffectorTrans.y = -n;
					} else { // Failsafe.
						localEffectorTrans.z = 0.0f;
						localEffectorTrans.y = -effectorLength;
					}

					return true;
				}

				return false;
			}

			static bool _PrefixLegEffectorPos_Upper_Circular_Far( ref Vector3 localEffectorTrans,
				float centerPositionZ,
				float effectorLengthZ, float effectorLengthY )
			{
				if( effectorLengthY > IKEpsilon && effectorLengthZ > IKEpsilon ) {
					float y = localEffectorTrans.y;
					float z = localEffectorTrans.z - centerPositionZ;

					y /= effectorLengthY;
					z /= effectorLengthZ;

					float len = Sqrt( y * y + z * z );
					if( len > 1.0f ) {
						float n = Sqrt( 1.0f - z * z );
						if( n > IKEpsilon ) { // Memo: Upper only.
							localEffectorTrans.y = n * effectorLengthY;
						} else { // Failsafe.
							localEffectorTrans.z = centerPositionZ;
							localEffectorTrans.y = effectorLengthY;
						}

						return true;
					}
				}

				return false;
			}

			//------------------------------------------------------------------------------------------------------------

			// for Arms.

			const float _LocalDirMaxTheta = 0.99f;
			const float _LocalDirLerpTheta = 0.01f;

			static bool _NormalizeXZ( ref Vector3 localDirXZ )
			{
				float t = localDirXZ.x * localDirXZ.x + localDirXZ.z * localDirXZ.z;
				if( t > IKEpsilon ) {
					t = (float)System.Math.Sqrt( (float)t ); // Faster than Mathf.Sqrt()
					if( t > IKEpsilon ) {
						t = 1.0f / t;
						localDirXZ.x *= t;
						localDirXZ.z *= t;
						return true;
					} else {
						return false;
					}
				} else {
					return false;
				}
			}

			// Lefthand based.
			static void _ComputeLocalDirXZ( ref Vector3 localDir, out Vector3 localDirXZ )
			{
				if( localDir.y >= _LocalDirMaxTheta - IKEpsilon ) {
					localDirXZ = new Vector3( 1.0f, 0.0f, 0.0f );
				} else if( localDir.y > _LocalDirMaxTheta - _LocalDirLerpTheta - IKEpsilon ) {
					float r = (localDir.y - (_LocalDirMaxTheta - _LocalDirLerpTheta)) * (1.0f / _LocalDirLerpTheta);
					localDirXZ = new Vector3( localDir.x + (1.0f - localDir.x) * r, 0.0f, localDir.z - localDir.z * r );
					if( !_NormalizeXZ( ref localDirXZ ) ) {
						localDirXZ = new Vector3( 1.0f, 0.0f, 0.0f );
					}
				} else if( localDir.y <= -_LocalDirMaxTheta + IKEpsilon ) {
					localDirXZ = new Vector3( -1.0f, 0.0f, 0.0f );
				} else if( localDir.y < -(_LocalDirMaxTheta - _LocalDirLerpTheta - IKEpsilon) ) {
					float r = (-(_LocalDirMaxTheta - _LocalDirLerpTheta) - localDir.y) * (1.0f / _LocalDirLerpTheta);
					localDirXZ = new Vector3( localDir.x + (-1.0f - localDir.x) * r, 0.0f, localDir.z - localDir.z * r );
					if( !_NormalizeXZ( ref localDirXZ ) ) {
						localDirXZ = new Vector3( -1.0f, 0.0f, 0.0f );
					}
				} else {
					localDirXZ = new Vector3( localDir.x, 0.0f, localDir.z );
					if( !_NormalizeXZ( ref localDirXZ ) ) {
						localDirXZ = new Vector3( 1.0f, 0.0f, 0.0f );
					}
				}
			}

			static bool _NormalizeYZ( ref Vector3 localDirYZ )
			{
				float t = localDirYZ.y * localDirYZ.y + localDirYZ.z * localDirYZ.z;
				if( t > IKEpsilon ) {
					t = (float)System.Math.Sqrt( (float)t ); // Faster than Mathf.Sqrt()
					if( t > IKEpsilon ) {
						t = 1.0f / t;
						localDirYZ.y *= t;
						localDirYZ.z *= t;
						return true;
					} else {
						return false;
					}
				} else {
					return false;
				}
			}

			// Lefthand based.
			static void _ComputeLocalDirYZ( ref Vector3 localDir, out Vector3 localDirYZ )
			{
				if( localDir.x >= _LocalDirMaxTheta - IKEpsilon ) {
					localDirYZ = new Vector3( 0.0f, 0.0f, -1.0f );
				} else if( localDir.x > _LocalDirMaxTheta - _LocalDirLerpTheta - IKEpsilon ) {
					float r = (localDir.x - (_LocalDirMaxTheta - _LocalDirLerpTheta)) * (1.0f / _LocalDirLerpTheta);
					localDirYZ = new Vector3( 0.0f, localDir.y - localDir.y * r, localDir.z + (-1.0f - localDir.z) * r );
					if( !_NormalizeYZ( ref localDirYZ ) ) {
						localDirYZ = new Vector3( 0.0f, 0.0f, -1.0f );
					}
				} else if( localDir.x <= -_LocalDirMaxTheta + IKEpsilon ) {
					localDirYZ = new Vector3( 0.0f, 0.0f, 1.0f );
				} else if( localDir.x < -(_LocalDirMaxTheta - _LocalDirLerpTheta - IKEpsilon) ) {
					float r = (-(_LocalDirMaxTheta - _LocalDirLerpTheta) - localDir.x) * (1.0f / _LocalDirLerpTheta);
					localDirYZ = new Vector3( 0.0f, localDir.y - localDir.y * r, localDir.z + (1.0f - localDir.z) * r );
					if( !_NormalizeYZ( ref localDirYZ ) ) {
						localDirYZ = new Vector3( 0.0f, 0.0f, 1.0f );
					}
				} else {
					localDirYZ = new Vector3( 0.0f, localDir.y, localDir.z );
					if( !_NormalizeYZ( ref localDirYZ ) ) {
						localDirYZ = new Vector3( 0.0f, 0.0f, (localDir.x >= 0.0f) ? -1.0f : 1.0f );
					}
				}
			}

			//------------------------------------------------------------------------------------------------------------

			CachedDegreesToCos _presolvedLerpTheta = CachedDegreesToCos.zero;
			CachedDegreesToCos _automaticKneeBaseTheta = CachedDegreesToCos.zero;
			CachedDegreesToCosSin _automaticArmElbowTheta = CachedDegreesToCosSin.zero;

			public bool Solve()
			{
				_UpdateArgs();
				_arm_isSolvedLimbIK = false;

				bool r = _SolveInternal();
				r |= _SolveEndRotation();
				r |= _TwistInternal();

				return r;
			}

			public bool _SolveInternal()
			{
				if( !_endEffector.positionEnabled ) {
					return false;
				}

				if( _beginBone.parentBone == null || !_beginBone.parentBone.transformIsAlive ) {
					return false; // Failsafe.
				}

				Matrix3x3 parentBaseBasis = _beginBone.parentBone.worldRotation * _beginBone.parentBone._worldToBaseRotation;
				Matrix3x3 parentBaseBasisInv = parentBaseBasis.transpose;

				Vector3 beginPos = _beginBone.worldPosition;
				Vector3 bendingPos = _bendingEffector._hidden_worldPosition;
				Vector3 effectorPos = _endEffector._hidden_worldPosition;
				Vector3 effectorTrans = effectorPos - beginPos;

				float effectorLen = effectorTrans.magnitude;
				if( effectorLen <= IKEpsilon ) {
					return _SolveEndRotation();
				}
				if( _effectorMaxLength.value <= IKEpsilon ) {
					return _SolveEndRotation();
				}

				Vector3 effectorDir = effectorTrans * (1.0f / effectorLen);

				if( effectorLen > _effectorMaxLength.value ) {
					effectorTrans = effectorDir * _effectorMaxLength.value;
					effectorPos = beginPos + effectorTrans;
					effectorLen = _effectorMaxLength.value;
				}

				Vector3 localEffectorDir = new Vector3( 0.0f, 0.0f, 1.0f );
				if( _limbIKType == LimbIKType.Arm ) {
					localEffectorDir = parentBaseBasisInv.Multiply( ref effectorDir );
				}

				// pending: Detail processing for Arm too.
				if( _limbIKType == LimbIKType.Leg && _settingsLimbIK.prefixLegEffectorEnabled ) { // Override Effector Pos.
					Vector3 localEffectorTrans = parentBaseBasisInv.Multiply( ref effectorTrans );

					bool isProcessed = false;
					bool isLimited = false;
					if( localEffectorTrans.z >= 0.0f ) { // Front
						if( localEffectorTrans.z >= _beginToBendingLength + _bendingToEndLength ) { // So far.
							isProcessed = true;
							localEffectorTrans.z = _beginToBendingLength + _bendingToEndLength;
							localEffectorTrans.y = 0.0f;
                        }

						if( !isProcessed &&
							localEffectorTrans.y >= -_effectorMinLength.value &&
							localEffectorTrans.z <= _leg_upperLimitNearCircleZ ) { // Upper(Near)
							isProcessed = true;
							isLimited = _PrefixLegEffectorPos_UpperNear( ref localEffectorTrans );
                        }

						if( !isProcessed &&
							localEffectorTrans.y >= 0.0f &&
							localEffectorTrans.z > _leg_upperLimitNearCircleZ ) { // Upper(Far)
							isProcessed = true;
							_PrefixLegEffectorPos_Upper_Circular_Far( ref localEffectorTrans,
								_leg_upperLimitNearCircleZ,
								_beginToBendingLength + _bendingToEndLength - _leg_upperLimitNearCircleZ,
								_leg_upperLimitNearCircleY );
                        }

						if( !isProcessed ) { // Lower
							isProcessed = true;
							isLimited = _PrefixLegEffectorPos_Circular_Far( ref localEffectorTrans, _beginToBendingLength + _bendingToEndLength );
                        }

					} else { // Back
						// Pending: Detail Processing.
						if( localEffectorTrans.y >= -_effectorMinLength.value ) {
							isLimited = true;
							localEffectorTrans.y = -_effectorMinLength.value;
                        } else {
							isLimited = _PrefixLegEffectorPos_Circular_Far( ref localEffectorTrans, _beginToBendingLength + _bendingToEndLength );
						}
					}

					if( isLimited ) {
#if SAFULLBODYIK_DEBUG
						_debugData.AddPoint( effectorPos, Color.black, 0.05f );
#endif
						effectorTrans = parentBaseBasis * localEffectorTrans;
						effectorLen = effectorTrans.magnitude;
						effectorPos = beginPos + effectorTrans;
						if( effectorLen > IKEpsilon ) {
							effectorDir = effectorTrans * (1.0f / effectorLen);
						}
#if SAFULLBODYIK_DEBUG
						_debugData.AddPoint( effectorPos, Color.white, 0.05f );
#endif
					}
				}

				Matrix3x3 baseBasis;
				_SolveBaseBasis( out baseBasis, ref parentBaseBasis, ref effectorDir );

				// Automatical bendingPos
				if( !_bendingEffector.positionEnabled ) {
					bool presolvedEnabled = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.presolveKneeEnabled : _settingsLimbIK.presolveElbowEnabled;
					float presolvedBendingRate = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.presolveKneeRate : _settingsLimbIK.presolveElbowRate;
					float presolvedLerpAngle = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.presolveKneeLerpAngle : _settingsLimbIK.presolveElbowLerpAngle;
					float presolvedLerpLengthRate = (_limbIKType == LimbIKType.Leg) ? _settingsLimbIK.presolveKneeLerpLengthRate : _settingsLimbIK.presolveElbowLerpLengthRate;

					Vector3 presolvedBendingPos = Vector3.zero;

					if( presolvedEnabled && _isPresolvedBending ) {
						if( _presolvedEffectorLength > IKEpsilon ) {
							float lerpLength = _presolvedEffectorLength * presolvedLerpLengthRate;
							if( lerpLength > IKEpsilon ) {
								float tempLength = Mathf.Abs( _presolvedEffectorLength - effectorLen );
								if( tempLength < lerpLength ) {
									presolvedBendingRate *= 1.0f - (tempLength / lerpLength);
                                } else {
									presolvedBendingRate = 0.0f;
								}
							} else { // Failsafe.
								presolvedBendingRate = 0.0f;
							}
						} else { // Failsafe.
							presolvedBendingRate = 0.0f;
						}

						if( presolvedBendingRate > IKEpsilon ) {
							_presolvedLerpTheta.Reset( presolvedLerpAngle );
							if( _presolvedLerpTheta < 1.0f - IKEpsilon ) { // Lerp
								float presolvedFeedbackTheta = Vector3.Dot( effectorDir, _presolvedEffectorDir );
								if( presolvedFeedbackTheta > _presolvedLerpTheta + IKEpsilon ) {
									float presolvedFeedbackRate = (presolvedFeedbackTheta - _presolvedLerpTheta) / (1.0f - _presolvedLerpTheta);
									presolvedBendingRate *= presolvedFeedbackRate;
								} else {
									presolvedBendingRate = 0.0f;
								}
							} else {
								presolvedBendingRate = 0.0f;
							}
						}

						if( presolvedBendingRate > IKEpsilon ) {
							Vector3 bendingDir;
							Matrix3x3 presolvedBendingBasis = baseBasis * _presolvedBendingBasis;
							if( _limbIKType == LimbIKType.Arm ) {
								bendingDir = (_limbIKSide == Side.Left) ? -presolvedBendingBasis.column0 : presolvedBendingBasis.column0;
							} else {
								bendingDir = -presolvedBendingBasis.column1;
							}

							presolvedBendingPos = beginPos + bendingDir * _beginToBendingLength;
							bendingPos = presolvedBendingPos; // Failsafe.
						}
					} else {
						presolvedBendingRate = 0.0f;
					}

					if( presolvedBendingRate < 1.0f - IKEpsilon ) {
						float cosTheta = ComputeCosTheta(
							_bendingToEndLengthSq,          // lenASq
							effectorLen * effectorLen,      // lenBSq
							_beginToBendingLengthSq,        // lenCSq
							effectorLen,                    // lenB
							_beginToBendingLength );        // lenC

						float sinTheta = Sqrt( Mathf.Clamp01( 1.0f - cosTheta * cosTheta ) );

						float moveC = _beginToBendingLength * (1.0f - Mathf.Max( _defaultCosTheta - cosTheta, 0.0f ));
						float moveS = _beginToBendingLength * Mathf.Max( sinTheta - _defaultSinTheta, 0.0f );

						if( _limbIKType == LimbIKType.Arm ) {
							Vector3 dirX = (_limbIKSide == Side.Left) ? -baseBasis.column0 : baseBasis.column0;
							{
								float elbowBaseAngle = _settingsLimbIK.automaticElbowBaseAngle;
								float elbowLowerAngle = _settingsLimbIK.automaticElbowLowerAngle;
								float elbowUpperAngle = _settingsLimbIK.automaticElbowUpperAngle;
								float elbowBackUpperAngle = _settingsLimbIK.automaticElbowBackUpperAngle;
								float elbowBackLowerAngle = _settingsLimbIK.automaticElbowBackLowerAngle;

								// Based on localXZ
								float armEffectorBackBeginSinTheta = _internalValuesLimbIK.armEffectorBackBeginTheta.sin;
								float armEffectorBackCoreBeginSinTheta = _internalValuesLimbIK.armEffectorBackCoreBeginTheta.sin;
								float armEffectorBackCoreEndCosTheta = _internalValuesLimbIK.armEffectorBackCoreEndTheta.cos;
								float armEffectorBackEndCosTheta = _internalValuesLimbIK.armEffectorBackEndTheta.cos;

								// Based on localYZ
								float armEffectorBackCoreUpperSinTheta = _internalValuesLimbIK.armEffectorBackCoreUpperTheta.sin;
								float armEffectorBackCoreLowerSinTheta = _internalValuesLimbIK.armEffectorBackCoreLowerTheta.sin;

								float elbowAngle = elbowBaseAngle;

								Vector3 localXZ; // X is reversed in RightSide.
								Vector3 localYZ;

								Vector3 localDir = (_limbIKSide == Side.Left) ? localEffectorDir : new Vector3( -localEffectorDir.x, localEffectorDir.y, localEffectorDir.z );
								_ComputeLocalDirXZ( ref localDir, out localXZ ); // Lefthand Based.
								_ComputeLocalDirYZ( ref localDir, out localYZ ); // Lefthand Based.

								if( localDir.y < 0.0f ) {
									elbowAngle = Mathf.Lerp( elbowAngle, elbowLowerAngle, -localDir.y );
								} else {
									elbowAngle = Mathf.Lerp( elbowAngle, elbowUpperAngle, localDir.y );
								}

								if( localXZ.z < armEffectorBackBeginSinTheta &&
									localXZ.x > armEffectorBackEndCosTheta ) {

									float targetAngle;
									if( localYZ.y >= armEffectorBackCoreUpperSinTheta ) {
										targetAngle = elbowBackUpperAngle;
									} else if( localYZ.y <= armEffectorBackCoreLowerSinTheta ) {
										targetAngle = elbowBackLowerAngle;
									} else {
										float t = armEffectorBackCoreUpperSinTheta - armEffectorBackCoreLowerSinTheta;
										if( t > IKEpsilon ) {
											float r = (localYZ.y - armEffectorBackCoreLowerSinTheta) / t;
											targetAngle = Mathf.Lerp( elbowBackLowerAngle, elbowBackUpperAngle, r );
										} else {
											targetAngle = elbowBackLowerAngle;
										}
									}

									if( localXZ.x < armEffectorBackCoreEndCosTheta ) {
										float t = armEffectorBackCoreEndCosTheta - armEffectorBackEndCosTheta;
										if( t > IKEpsilon ) {
											float r = (localXZ.x - armEffectorBackEndCosTheta) / t;

											if( localYZ.y <= armEffectorBackCoreLowerSinTheta ) {
												elbowAngle = Mathf.Lerp( elbowAngle, targetAngle, r );
											} else if( localYZ.y >= armEffectorBackCoreUpperSinTheta ) {
												elbowAngle = Mathf.Lerp( elbowAngle, targetAngle - 360.0f, r );
											} else {
												float angle0 = Mathf.Lerp( elbowAngle, targetAngle, r ); // Lower
												float angle1 = Mathf.Lerp( elbowAngle, targetAngle - 360.0f, r ); // Upper
												float t2 = armEffectorBackCoreUpperSinTheta - armEffectorBackCoreLowerSinTheta;
												if( t2 > IKEpsilon ) {
													float r2 = (localYZ.y - armEffectorBackCoreLowerSinTheta) / t2;
													if( angle0 - angle1 > 180.0f ) {
														angle1 += 360.0f;
													}

													elbowAngle = Mathf.Lerp( angle0, angle1, r2 );
												} else { // Failsafe.
													elbowAngle = angle0;
												}
											}
										}
									} else if( localXZ.z > armEffectorBackCoreBeginSinTheta ) {
										float t = (armEffectorBackBeginSinTheta - armEffectorBackCoreBeginSinTheta);
										if( t > IKEpsilon ) {
											float r = (armEffectorBackBeginSinTheta - localXZ.z) / t;
											if( localDir.y >= 0.0f ) {
												elbowAngle = Mathf.Lerp( elbowAngle, targetAngle, r );
											} else {
												elbowAngle = Mathf.Lerp( elbowAngle, targetAngle - 360.0f, r );
											}
										} else { // Failsafe.
											elbowAngle = targetAngle;
										}
									} else {
										elbowAngle = targetAngle;
									}
								}

								Vector3 dirY = parentBaseBasis.column1;
								Vector3 dirZ = Vector3.Cross( baseBasis.column0, dirY );
								dirY = Vector3.Cross( dirZ, baseBasis.column0 );
								if( !_SafeNormalize( ref dirY, ref dirZ ) ) { // Failsafe.
									dirY = parentBaseBasis.column1;
									dirZ = parentBaseBasis.column2;
								}

								if( _automaticArmElbowTheta._degrees != elbowAngle ) {
									_automaticArmElbowTheta._Reset( elbowAngle );
                                }

								bendingPos = beginPos + dirX * moveC
									+ -dirY * moveS * _automaticArmElbowTheta.cos
									+ -dirZ * moveS * _automaticArmElbowTheta.sin;
							}
						} else { // Leg
							float automaticKneeBaseAngle = _settings.limbIK.automaticKneeBaseAngle;
                            if( automaticKneeBaseAngle >= -IKEpsilon && automaticKneeBaseAngle <= IKEpsilon ) { // Fuzzy 0
								bendingPos = beginPos + -baseBasis.column1 * moveC + baseBasis.column2 * moveS;
							} else {
								if( _automaticKneeBaseTheta._degrees != automaticKneeBaseAngle ) {
									_automaticKneeBaseTheta._Reset( automaticKneeBaseAngle );
								}

								float kneeSin = _automaticKneeBaseTheta.cos;
                                float kneeCos = Sqrt( 1.0f - kneeSin * kneeSin );
								if( _limbIKSide == Side.Right ) {
									if( automaticKneeBaseAngle >= 0.0f ) {
										kneeCos = -kneeCos;
									}
								} else {
									if( automaticKneeBaseAngle < 0.0f ) {
										kneeCos = -kneeCos;
									}
								}

								bendingPos = beginPos + -baseBasis.column1 * moveC
									+ baseBasis.column0 * moveS * kneeCos
									+ baseBasis.column2 * moveS * kneeSin;
							}
						}
					}

					if( presolvedBendingRate > IKEpsilon ) {
						bendingPos = Vector3.Lerp( bendingPos, presolvedBendingPos, presolvedBendingRate );
                    }
				}

				bool isSolved = false;
				Vector3 solvedBeginToBendingDir = Vector3.zero;
				Vector3 solvedBendingToEndDir = Vector3.zero;

				{
					Vector3 beginToBendingTrans = bendingPos - beginPos;
					Vector3 intersectBendingTrans = beginToBendingTrans - effectorDir * Vector3.Dot( effectorDir, beginToBendingTrans );
					float intersectBendingLen = intersectBendingTrans.magnitude;

					if( intersectBendingLen > IKEpsilon ) {
						Vector3 intersectBendingDir = intersectBendingTrans * (1.0f / intersectBendingLen);

						float bc2 = 2.0f * _beginToBendingLength * effectorLen;
						if( bc2 > IKEpsilon ) {
							float effectorCosTheta = (_beginToBendingLengthSq + effectorLen * effectorLen - _bendingToEndLengthSq) / bc2;
							float effectorSinTheta = Sqrt( Mathf.Clamp01( 1.0f - effectorCosTheta * effectorCosTheta ) );

							Vector3 beginToInterTranslate = effectorDir * effectorCosTheta * _beginToBendingLength
															+ intersectBendingDir * effectorSinTheta * _beginToBendingLength;
							Vector3 interToEndTranslate = effectorPos - (beginPos + beginToInterTranslate);

							if( _SafeNormalize( ref beginToInterTranslate, ref interToEndTranslate ) ) {
								isSolved = true;
								solvedBeginToBendingDir = beginToInterTranslate;
								solvedBendingToEndDir = interToEndTranslate;
							}
						}
					}
				}

				if( isSolved && _limbIKType == LimbIKType.Arm ) {
					float elbowFrontInnerLimitSinTheta = _internalValuesLimbIK.elbowFrontInnerLimitTheta.sin;
					float elbowBackInnerLimitSinTheta = _internalValuesLimbIK.elbowBackInnerLimitTheta.sin;

					Vector3 localBendingDir = parentBaseBasisInv.Multiply( ref solvedBeginToBendingDir );
					bool isBack = localBendingDir.z < 0.0f;
					float limitTheta = isBack ? elbowBackInnerLimitSinTheta : elbowFrontInnerLimitSinTheta;

                    float localX = (_limbIKSide == Side.Left) ? localBendingDir.x : (-localBendingDir.x);
					if( localX > limitTheta ) {
						localBendingDir.x = (_limbIKSide == Side.Left) ? limitTheta : -limitTheta;
                        localBendingDir.z = Sqrt( 1.0f - (localBendingDir.x * localBendingDir.x + localBendingDir.y * localBendingDir.y) );
						if( isBack ) {
							localBendingDir.z = -localBendingDir.z;
                        }
						Vector3 bendingDir = parentBaseBasis.Multiply( localBendingDir );
						Vector3 interPos = beginPos + bendingDir * _beginToBendingLength;
						Vector3 endDir = effectorPos - interPos;
						if( _SafeNormalize( ref endDir ) ) {
							solvedBeginToBendingDir = bendingDir;
							solvedBendingToEndDir = endDir;
						}
					}
                }

				if( !isSolved ) { // Failsafe.
					Vector3 bendingDir = bendingPos - beginPos;
					if( _SafeNormalize( ref bendingDir ) ) {
						Vector3 interPos = beginPos + bendingDir * _beginToBendingLength;
						Vector3 endDir = effectorPos - interPos;
						if( _SafeNormalize( ref endDir ) ) {
							isSolved = true;
							solvedBeginToBendingDir = bendingDir;
							solvedBendingToEndDir = endDir;
						}
					}
				}

				if( !isSolved ) {
					return false;
				}

				Matrix3x3 beginBasis = Matrix3x3.identity;
				Matrix3x3 bendingBasis = Matrix3x3.identity;

				if( _limbIKType == LimbIKType.Arm ) {
					// Memo: Arm Bone Based Y Axis.
					if( _limbIKSide == Side.Left ) {
						solvedBeginToBendingDir = -solvedBeginToBendingDir;
						solvedBendingToEndDir = -solvedBendingToEndDir;
					}

					Vector3 basisY = parentBaseBasis.column1;
					Vector3 basisZ = parentBaseBasis.column2;
					if( !_ComputeBasisLockX( out beginBasis, ref solvedBeginToBendingDir, ref basisY, ref basisZ ) ) {
						return false;
					}

					{
						if( effectorLen > _arm_elbowBasisForcefixEffectorLengthEnd.value ) {
							basisY = beginBasis.Multiply_Column1( ref _beginToBendingBoneBasis );
						} else {
							basisY = Vector3.Cross( -solvedBeginToBendingDir, solvedBendingToEndDir ); // Memo: Require to MaxEffectorLengthRate is less than 1.0
							if( _limbIKSide == Side.Left ) {
								basisY = -basisY;
							}

							if( effectorLen > _arm_elbowBasisForcefixEffectorLengthBegin.value ) {
								float t = _arm_elbowBasisForcefixEffectorLengthEnd.value - _arm_elbowBasisForcefixEffectorLengthBegin.value;
								if( t > IKEpsilon ) {
									float r = (effectorLen - _arm_elbowBasisForcefixEffectorLengthBegin.value) / t;
									basisY = Vector3.Lerp( basisY, beginBasis.Multiply_Column1( ref _beginToBendingBoneBasis ), r );
                                }
                            }
						}

						if( !_ComputeBasisFromXYLockX( out bendingBasis, ref solvedBendingToEndDir, ref basisY ) ) {
							return false;
						}
					}
				} else {
					// Memo: Leg Bone Based X Axis.
					solvedBeginToBendingDir = -solvedBeginToBendingDir;
					solvedBendingToEndDir = -solvedBendingToEndDir;

					Vector3 basisX = baseBasis.column0;
					Vector3 basisZ = baseBasis.column2;
					if( !_ComputeBasisLockY( out beginBasis, ref basisX, ref solvedBeginToBendingDir, ref basisZ ) ) {
						return false;
					}

					basisX = beginBasis.Multiply_Column0( ref _beginToBendingBoneBasis );
					if( !_ComputeBasisFromXYLockY( out bendingBasis, ref basisX, ref solvedBendingToEndDir ) ) {
						return false;
					}
				}

				if( _limbIKType == LimbIKType.Arm ) {
					_arm_isSolvedLimbIK = true;
					_arm_solvedBeginBoneBasis = beginBasis;
					_arm_solvedBendingBoneBasis = bendingBasis;
				}

				_beginBone.worldRotation = (beginBasis * _beginBone._boneToWorldBasis).GetRotation();
				_bendingBone.worldRotation = (bendingBasis * _bendingBone._boneToWorldBasis).GetRotation();
				return true;
			}

			bool _SolveEndRotation()
			{
				if( !_endEffector.rotationEnabled ) {
					return false;
				}

				var r = _endEffector.worldRotation * _endEffectorToWorldRotation;
				_endBone.worldRotation = r;
				return true;
			}

			bool _TwistInternal()
			{
				if( _limbIKType != LimbIKType.Arm || !_settings.twistEnabled ) {
					return false;
				}

				bool isSolved = false;

				if( _armTwistBones != null && _armTwistBones.Length > 0 ) {
					int boneLength = _armTwistBones.Length;

					Matrix3x3 beginBoneBasis;
					Matrix3x3 bendingBoneBasis; // Attension: bendingBoneBasis is based on beginBoneBasis
					if( _arm_isSolvedLimbIK ) {
						beginBoneBasis = _arm_solvedBeginBoneBasis;
						bendingBoneBasis = _arm_solvedBendingBoneBasis * _arm_bendingToBeginBoneBasis;
					} else {
						beginBoneBasis = new Matrix3x3( _beginBone.worldRotation * _beginBone._worldToBoneRotation );
						bendingBoneBasis = new Matrix3x3( _bendingBone.worldRotation * _arm_bendingWorldToBeginBoneRotation );
					}

					Vector3 dirX = beginBoneBasis.column0;
					Vector3 dirY = bendingBoneBasis.column1;
					Vector3 dirZ = bendingBoneBasis.column2;

					Matrix3x3 bendingBasisTo;

					if( _ComputeBasisLockX( out bendingBasisTo, ref dirX, ref dirY, ref dirZ ) ) {
						var baseBasis = beginBoneBasis * _beginBone._boneToBaseBasis;
						var baseBasisTo = bendingBasisTo * _beginBone._boneToBaseBasis;

						for( int i = 0; i < boneLength; ++i ) {
							if( _armTwistBones[i].bone != null && _armTwistBones[i].bone.transformIsAlive ) {
								Matrix3x3 tempBasis;
								float rate = _armTwistBones[i].rate;
								_Lerp( out tempBasis, ref baseBasis, ref baseBasisTo, rate );
								_armTwistBones[i].bone.worldRotation = (tempBasis * _handTwistBones[i].bone._baseToWorldBasis).GetRotation();
								isSolved = true;
							}
						}
					}
				}

				if( _handTwistBones != null && _handTwistBones.Length > 0 ) {
					int boneLength = _handTwistBones.Length;

					Matrix3x3 bendingBoneBasis;
					if( _arm_isSolvedLimbIK ) {
						bendingBoneBasis = _arm_solvedBendingBoneBasis;
                    } else {
						bendingBoneBasis = new Matrix3x3( _bendingBone.worldRotation * _bendingBone._worldToBoneRotation );
					}

					// Attension: endBoneBasis is based on bendingBoneBasis
					Matrix3x3 endBoneBasis = new Matrix3x3( _endBone.worldRotation * _arm_endWorldToBendingBoneRotation );
					
					Vector3 dirZ = endBoneBasis.column2;
					Vector3 dirX = bendingBoneBasis.column0;
					Vector3 dirY = Vector3.Cross( dirZ, dirX );
					dirZ = Vector3.Cross( dirX, dirY );
					if( _SafeNormalize( ref dirY, ref dirZ ) ) { // Lock dirX(bendingBoneBasis.column0)
						var baseBasis = bendingBoneBasis * _bendingBone._boneToBaseBasis;
						var baseBasisTo = Matrix3x3.FromColumn( dirX, dirY, dirZ ) * _bendingBone._boneToBaseBasis;

						for( int i = 0; i < boneLength; ++i ) {
							if( _handTwistBones[i].bone != null && _handTwistBones[i].bone.transformIsAlive ) {
								Matrix3x3 tempBasis;
								float rate = _handTwistBones[i].rate;
								_Lerp( out tempBasis, ref baseBasis, ref baseBasisTo, rate );
								_handTwistBones[i].bone.worldRotation = (tempBasis * _handTwistBones[i].bone._baseToWorldBasis).GetRotation();
								isSolved = true;
                            }
						}
					}
				}

				return isSolved;
			}
		}
	}
}