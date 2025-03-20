use super::fields::AssetData;
use sqlx::{
    encode::IsNull, sqlite::SqliteValueRef, Decode, Encode, Sqlite, Type, TypeInfo, ValueRef,
};
use std::{error::Error, fmt};

impl<'r> Decode<'r, Sqlite> for AssetData {
    fn decode(
        value: SqliteValueRef<'r>,
    ) -> Result<AssetData, Box<(dyn Error + Send + Sync + 'static)>> {
        match value.type_info().name() {
            "BLOB" => Ok(AssetData::Blob(
                (<Vec<u8> as Decode<Sqlite>>::decode(value))?,
            )),
            "TEXT" => Ok(AssetData::String(
                (<String as Decode<Sqlite>>::decode(value))?,
            )),
            _ => Err("Database column is an unsupported type.".into()),
        }
    }
}

impl<'r> Encode<'r, Sqlite> for AssetData {
    fn encode_by_ref(
        &self,
        buf: &mut <Sqlite as sqlx::Database>::ArgumentBuffer<'r>,
    ) -> Result<IsNull, sqlx::error::BoxDynError> {
        match &self {
            AssetData::Blob(b) => <Vec<u8> as Encode<Sqlite>>::encode_by_ref(b, buf),
            AssetData::String(s) => <String as Encode<Sqlite>>::encode_by_ref(s, buf),
        }
    }

    fn produces(&self) -> Option<<Sqlite as sqlx::Database>::TypeInfo> {
        match &self {
            AssetData::Blob(_) => Some(<Vec<u8> as Type<Sqlite>>::type_info()),
            AssetData::String(_) => Some(<String as Type<Sqlite>>::type_info()),
        }
    }
}

impl fmt::Display for AssetData {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.pad(self.name())
    }
}

impl Type<Sqlite> for AssetData {
    /// Default database type is BLOB, but actual database type is BLOB or TEXT. Depends on enum variant. This is implemented in Encode.produces.
    fn type_info() -> sqlx::sqlite::SqliteTypeInfo {
        <Vec<u8> as Type<Sqlite>>::type_info()
    }

    /// Always returns true because the asset_data column uses SQLite's ANY type
    fn compatible(_ty: &<Sqlite as sqlx::Database>::TypeInfo) -> bool {
        true
    }
}

impl TypeInfo for AssetData {
    fn is_null(&self) -> bool {
        false
    }

    fn name(&self) -> &str {
        match &self {
            AssetData::Blob(_) => "BLOB",
            AssetData::String(_) => "TEXT",
        }
    }
}
