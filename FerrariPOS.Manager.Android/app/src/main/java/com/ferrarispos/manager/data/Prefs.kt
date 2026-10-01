package com.ferrarispos.manager.data

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first

private val Context.dataStore by preferencesDataStore("ferrari_manager")
class Prefs(private val context: Context) {
    private val urlKey = stringPreferencesKey("base_url")
    private val tokenKey = stringPreferencesKey("token")
    private val fallbackUrlKey = stringPreferencesKey("fallback_url")
    private val themeKey = stringPreferencesKey("theme_name")
    private val themeConfiguredKey = booleanPreferencesKey("theme_configured")
    private val soundsKey = booleanPreferencesKey("interface_sounds")
    private val haloKey = stringPreferencesKey("title_halo")
    suspend fun save(baseUrl: String, token: String, fallbackUrl: String = "") { context.dataStore.edit { it[urlKey] = baseUrl.ensureSlash(); it[tokenKey] = token; if (fallbackUrl.isNotBlank()) it[fallbackUrlKey] = fallbackUrl.ensureSlash() else it.remove(fallbackUrlKey) } }
    suspend fun clear() { context.dataStore.edit { it.clear() } }
    suspend fun url(): String = context.dataStore.data.first()[urlKey].orEmpty()
    suspend fun token(): String = context.dataStore.data.first()[tokenKey].orEmpty()
    suspend fun fallbackUrl(): String = context.dataStore.data.first()[fallbackUrlKey].orEmpty()
    suspend fun paired(): Boolean = url().isNotBlank() && token().isNotBlank()
    suspend fun theme(): String {
        val prefs = context.dataStore.data.first()
        return if (prefs[themeConfiguredKey] == true) prefs[themeKey] ?: "Rojo Ferrari" else "Rojo Ferrari"
    }
    suspend fun sounds(): Boolean = context.dataStore.data.first()[soundsKey] ?: true
    suspend fun saveTheme(value:String) { context.dataStore.edit { it[themeKey]=value; it[themeConfiguredKey]=true } }
    suspend fun saveSounds(value:Boolean) { context.dataStore.edit { it[soundsKey]=value } }
    suspend fun halo(): String = context.dataStore.data.first()[haloKey] ?: "Verde Flúor"
    suspend fun saveHalo(value:String) { context.dataStore.edit { it[haloKey]=value } }
}
private fun String.ensureSlash() = if (endsWith("/")) this else "$this/"
