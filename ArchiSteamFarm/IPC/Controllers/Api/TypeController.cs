//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/Type")]
	public sealed class TypeController : ArchiController {
		/// <summary>
		///     Fetches type info of given type.
		/// </summary>
		/// <remarks>
		///     Type info is defined as a representation of given object with its fields and properties being assigned to a string value that defines their type.
		/// </remarks>
		[HttpGet("{type:required}")]
		[ProducesResponseType(typeof(GenericResponse<TypeResponse>), 200)]
		public ActionResult<GenericResponse<TypeResponse>> TypeGet(string type) {
			if (string.IsNullOrEmpty(type)) {
				ASF.ArchiLogger.LogNullError(nameof(type));
				return BadRequest(new GenericResponse<TypeResponse>(false, string.Format(Strings.ErrorIsEmpty, nameof(type))));
			}

			Type targetType = WebUtilities.ParseType(type);

			if (targetType == null) {
				return BadRequest(new GenericResponse<object>(false, string.Format(Strings.ErrorIsInvalid, type)));
			}

			string baseType = targetType.BaseType?.GetUnifiedName();
			HashSet<string> customAttributes = targetType.CustomAttributes.Select(attribute => attribute.AttributeType.GetUnifiedName()).ToHashSet();
			string underlyingType = null;

			Dictionary<string, string> body = new Dictionary<string, string>();

			if (targetType.IsClass) {
				foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(field => !field.IsPrivate)) {
					JsonPropertyAttribute jsonProperty = field.GetCustomAttribute<JsonPropertyAttribute>();

					if (jsonProperty != null) {
						body[jsonProperty.PropertyName ?? field.Name] = field.FieldType.GetUnifiedName();
					}
				}

				foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(property => property.CanRead && !property.GetMethod.IsPrivate)) {
					JsonPropertyAttribute jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();

					if (jsonProperty != null) {
						body[jsonProperty.PropertyName ?? property.Name] = property.PropertyType.GetUnifiedName();
					}
				}
			} else if (targetType.IsEnum) {
				Type enumType = Enum.GetUnderlyingType(targetType);
				underlyingType = enumType.GetUnifiedName();

				foreach (object value in Enum.GetValues(targetType)) {
					body[value.ToString()] = Convert.ChangeType(value, enumType).ToString();
				}
			}

			TypeResponse.TypeProperties properties = new TypeResponse.TypeProperties(baseType, customAttributes.Count > 0 ? customAttributes : null, underlyingType);

			TypeResponse response = new TypeResponse(body, properties);
			return Ok(new GenericResponse<TypeResponse>(response));
		}
	}
}
