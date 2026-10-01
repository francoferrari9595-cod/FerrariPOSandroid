package com.ferrarispos.manager.scanner

import android.Manifest
import android.content.pm.PackageManager
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.draw.scale
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

@Composable
fun BarcodeScannerScreen(title: String, onResult: (String) -> Unit, onClose: () -> Unit) {
    val context=LocalContext.current
    val lifecycle=LocalLifecycleOwner.current
    var granted by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED) }
    val launcher=rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted=it }
    LaunchedEffect(Unit) { if(!granted) launcher.launch(Manifest.permission.CAMERA) }

    Box(Modifier.fillMaxSize().background(Color(0xFF080B12))) {
        if(granted) {
            AndroidView(factory={ ctx ->
                val view=PreviewView(ctx)
                val providerFuture=ProcessCameraProvider.getInstance(ctx)
                providerFuture.addListener({
                    val provider=providerFuture.get()
                    val preview=Preview.Builder().build().also { it.setSurfaceProvider(view.surfaceProvider) }
                    val analysis=ImageAnalysis.Builder()
                        .setTargetResolution(Size(1280,720))
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
                    val executor=Executors.newSingleThreadExecutor()
                    val scanner=BarcodeScanning.getClient()
                    val delivered=AtomicBoolean(false)
                    analysis.setAnalyzer(executor) { proxy ->
                        val media=proxy.image
                        if(media!=null) {
                            val image=InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
                            scanner.process(image).addOnSuccessListener { codes ->
                                val value=codes.firstOrNull()?.rawValue
                                if(!value.isNullOrBlank() && delivered.compareAndSet(false,true)) onResult(value)
                            }.addOnCompleteListener { proxy.close() }
                        } else proxy.close()
                    }
                    provider.unbindAll()
                    provider.bindToLifecycle(lifecycle, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
                }, ContextCompat.getMainExecutor(ctx))
                view
            }, Modifier.fillMaxSize())
        } else {
            Column(Modifier.fillMaxSize(), horizontalAlignment=Alignment.CenterHorizontally, verticalArrangement=Arrangement.Center) {
                Text("Se necesita acceso a la cámara.", color=Color.White)
                Spacer(Modifier.height(12.dp))
                Button({ launcher.launch(Manifest.permission.CAMERA) }) { Text("PERMITIR CÁMARA") }
            }
        }
        Column(Modifier.fillMaxWidth().align(Alignment.TopCenter).padding(18.dp)) {
            Text(title, color=Color.White, style=MaterialTheme.typography.titleLarge)
            Text("Apuntá al QR o código de barras.", color=Color.LightGray)
        }
        OutlinedButton(onClose, Modifier.align(Alignment.BottomCenter).padding(24.dp)) { Text("CANCELAR") }
    }
}


@Composable
fun BarcodeMassScannerScreen(
    title: String,
    previewItems: List<String>,
    onPreviewClick: (Int) -> Unit,
    onResult: (String) -> Unit,
    onStop: () -> Unit
) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current
    val haptic = LocalHapticFeedback.current
    var granted by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED) }
    val launcher=rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted=it }
    LaunchedEffect(Unit) { if(!granted) launcher.launch(Manifest.permission.CAMERA) }
    Box(Modifier.fillMaxSize().background(Color.Black)) {
        if(granted) {
            AndroidView(factory={ ctx ->
                val view=PreviewView(ctx)
                val providerFuture=ProcessCameraProvider.getInstance(ctx)
                providerFuture.addListener({
                    val provider=providerFuture.get()
                    val preview=Preview.Builder().build().also { it.setSurfaceProvider(view.surfaceProvider) }
                    val analysis=ImageAnalysis.Builder()
                        .setTargetResolution(Size(1280,720))
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
                    val executor=Executors.newSingleThreadExecutor()
                    val scanner=BarcodeScanning.getClient()
                    val lastSeen=HashMap<String,Long>()
                    analysis.setAnalyzer(executor) { proxy ->
                        val media=proxy.image
                        if(media!=null) {
                            val image=InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
                            scanner.process(image).addOnSuccessListener { codes ->
                                val now=System.currentTimeMillis()
                                codes.mapNotNull { it.rawValue?.trim() }.distinct().forEach { value ->
                                    val previous=lastSeen[value] ?: 0L
                                    if(now-previous>1200L){
                                        lastSeen[value]=now
                                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                        onResult(value)
                                    }
                                }
                            }.addOnCompleteListener { proxy.close() }
                        } else proxy.close()
                    }
                    provider.unbindAll()
                    provider.bindToLifecycle(lifecycle, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
                }, ContextCompat.getMainExecutor(ctx))
                view
            }, Modifier.fillMaxSize())
        } else {
            Column(Modifier.fillMaxSize(), horizontalAlignment=Alignment.CenterHorizontally, verticalArrangement=Arrangement.Center) {
                Text("Se necesita acceso a la cámara.", color=Color.White)
                Spacer(Modifier.height(12.dp))
                Button({ launcher.launch(Manifest.permission.CAMERA) }) { Text("PERMITIR CÁMARA") }
            }
        }

        // Overlay transparente: la cámara permanece completamente visible detrás.
        Column(
            Modifier.fillMaxWidth()
                .align(Alignment.TopCenter)
                .padding(14.dp)
        ) {
            Surface(
                color=Color.Black.copy(alpha=.42f),
                shape=RoundedCornerShape(18.dp),
                border=BorderStroke(1.dp, Color.White.copy(alpha=.10f))
            ) {
                Column(Modifier.fillMaxWidth().padding(horizontal=16.dp, vertical=12.dp)) {
                    Text(title, color=Color.White, fontWeight=FontWeight.Black, style=MaterialTheme.typography.titleLarge)
                    Text("Pasá los productos uno detrás de otro", color=Color.White.copy(alpha=.72f), style=MaterialTheme.typography.bodySmall)
                    if(previewItems.isNotEmpty()) {
                        Spacer(Modifier.height(8.dp))
                        LazyColumn(
                            modifier=Modifier.fillMaxWidth().heightIn(max=300.dp),
                            verticalArrangement=Arrangement.spacedBy(4.dp)
                        ) {
                            val startIndex = maxOf(0, previewItems.size - 12)
                            itemsIndexed(previewItems.takeLast(12)) { localIndex, item ->
                                val realIndex = startIndex + localIndex
                                Row(
                                    Modifier.fillMaxWidth()
                                        .background(Color.Black.copy(alpha=.24f), RoundedCornerShape(10.dp))
                                        .clickable { onPreviewClick(realIndex) }
                                        .padding(horizontal=10.dp, vertical=7.dp),
                                    verticalAlignment=Alignment.CenterVertically
                                ) {
                                    Text("${realIndex+1}", color=Color(0xFF35E7FF), fontWeight=FontWeight.Black, modifier=Modifier.width(28.dp))
                                    Text(item, color=Color.White.copy(alpha=.94f), fontWeight=FontWeight.SemiBold, maxLines=1, modifier=Modifier.weight(1f))
                                    Text("EDITAR", color=Color.White.copy(alpha=.65f), fontSize=10.sp, fontWeight=FontWeight.Bold)
                                }
                            }
                        }
                    }
                }
            }
        }

        Button(
            onClick=onStop,
            modifier=Modifier.align(Alignment.BottomCenter).padding(18.dp).fillMaxWidth().height(58.dp),
            shape=RoundedCornerShape(18.dp),
            colors=ButtonDefaults.buttonColors(containerColor=Color(0xFFFF1744))
        ) {
            Text("⏹ PARAR ESCANEO Y CARGAR AL CARRITO", color=Color.White, fontWeight=FontWeight.Black)
        }
    }
}
