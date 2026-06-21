package com.dingleinc.texttoolspro

import android.content.Context
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dingleinc.texttoolspro.ui.theme.ExpandroidTheme
import com.dingleinc.texttoolspro.ui.MainScreen
import com.dingleinc.texttoolspro.ui.MainViewModel
import java.util.Locale

class MainActivity : ComponentActivity() {

    companion object {
        private const val PREFS_NAME = "settings"
        private const val KEY_LANG = "app_language"
    }

    override fun attachBaseContext(newBase: Context) {
        val prefs = newBase.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val langCode = prefs.getString(KEY_LANG, "en") ?: "en"
        val locale = if (langCode == "zh") Locale.CHINESE else Locale.ENGLISH
        val config = newBase.resources.configuration
        config.setLocale(locale)
        val context = newBase.createConfigurationContext(config)
        super.attachBaseContext(context)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val viewModel: MainViewModel = viewModel()
            val themeMode by viewModel.themeModeFlow.collectAsState()
            ExpandroidTheme(themeMode = themeMode) {
                MainScreen(viewModel = viewModel)
            }
        }
    }
}
