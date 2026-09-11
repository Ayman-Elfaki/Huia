<script setup lang="ts">
import { ref, onMounted } from 'vue'

const { user, loggedIn, session } = useUserSession()
const { login, register, logout, phoneLoginStart, phoneLoginVerify } = useAuth()

const activeTab = ref<'products' | 'login' | 'register' | 'phone' | 'cart'>('products')

// Email/Password login state
const email = ref('customer@shop.test')
const password = ref('Password1!2345')
const loginError = ref('')
const loginLoading = ref(false)

// Registration state
const regEmail = ref('')
const regPassword = ref('')
const regMessage = ref('')
const regError = ref('')
const regLoading = ref(false)

// Phone login state
const phoneNumber = ref('+15551234567')
const otpCode = ref('')
const otpSent = ref(false)
const phoneError = ref('')
const phoneLoading = ref(false)

// Products and Cart state
interface Product {
  id: string
  name: string
  price: number
  inStock: boolean
}
const products = ref<Product[]>([
  { id: 'prod-1', name: 'Huia Mechanical Keyboard', price: 149.99, inStock: true },
  { id: 'prod-2', name: 'Wireless Ergonomic Mouse', price: 79.99, inStock: true },
  { id: 'prod-3', name: '4K Ultra-Wide Monitor', price: 599.99, inStock: true },
  { id: 'prod-4', name: 'Desk Mat (Dark Charcoal)', price: 29.99, inStock: true },
])
const cartItems = ref<{ productId: string, name: string, quantity: number, unitPrice: number }[]>([])

async function handleLogin() {
  loginLoading.value = true
  loginError.value = ''
  try {
    const res = await login({ email: email.value, password: password.value })
    if (res.ok) {
      activeTab.value = 'products'
    }
  } catch (err: any) {
    loginError.value = err.data?.statusMessage || err.message || 'Login failed'
  } finally {
    loginLoading.value = false
  }
}

async function handleRegister() {
  regLoading.value = true
  regError.value = ''
  regMessage.value = ''
  try {
    const res = await register({ email: regEmail.value, password: regPassword.value })
    regMessage.value = res.message || 'Registration successful! You can now sign in.'
    email.value = regEmail.value
    password.value = regPassword.value
    activeTab.value = 'login'
  } catch (err: any) {
    regError.value = err.data?.statusMessage || err.message || 'Registration failed'
  } finally {
    regLoading.value = false
  }
}

async function handlePhoneStart() {
  phoneLoading.value = true
  phoneError.value = ''
  try {
    await phoneLoginStart({ phoneNumber: phoneNumber.value })
    otpSent.value = true
  } catch (err: any) {
    phoneError.value = err.data?.statusMessage || err.message || 'Failed to send OTP'
  } finally {
    phoneLoading.value = false
  }
}

async function handlePhoneVerify() {
  phoneLoading.value = true
  phoneError.value = ''
  try {
    const res = await phoneLoginVerify({ phoneNumber: phoneNumber.value, code: otpCode.value })
    if (res.ok) {
      activeTab.value = 'products'
    }
  } catch (err: any) {
    phoneError.value = err.data?.statusMessage || err.message || 'OTP verification failed'
  } finally {
    phoneLoading.value = false
  }
}

async function handleLogout() {
  await logout()
  cartItems.value = []
  activeTab.value = 'products'
}

function addToCart(p: Product) {
  const existing = cartItems.value.find(i => i.productId === p.id)
  if (existing) {
    existing.quantity++
  } else {
    cartItems.value.push({ productId: p.id, name: p.name, quantity: 1, unitPrice: p.price })
  }
}
</script>

<template>
  <div class="shop-container">
    <header class="header">
      <div class="brand">
        <div class="logo">H</div>
        <div>
          <h1>Huia Shop</h1>
          <span class="badge">Headless Bearer Token Auth</span>
        </div>
      </div>
      <nav class="nav">
        <button :class="{ active: activeTab === 'products' }" @click="activeTab = 'products'">Products</button>
        <button v-if="loggedIn" :class="{ active: activeTab === 'cart' }" @click="activeTab = 'cart'">
          Cart ({{ cartItems.reduce((s, i) => s + i.quantity, 0) }})
        </button>
        <template v-if="!loggedIn">
          <button :class="{ active: activeTab === 'login' }" @click="activeTab = 'login'">Login</button>
          <button :class="{ active: activeTab === 'register' }" @click="activeTab = 'register'">Register</button>
          <button :class="{ active: activeTab === 'phone' }" @click="activeTab = 'phone'">Phone OTP</button>
        </template>
        <template v-else>
          <div class="user-pill">
            <span class="user-sub">{{ user?.email || user?.sub }}</span>
            <button class="btn-logout" @click="handleLogout">Logout</button>
          </div>
        </template>
      </nav>
    </header>

    <main class="main">
      <!-- Products View -->
      <section v-if="activeTab === 'products'" class="view-section">
        <h2>Featured Hardware</h2>
        <p class="subtitle">Direct bearer token API authentication. Session securely sealed in server storage.</p>

        <div class="product-grid">
          <div v-for="p in products" :key="p.id" class="product-card">
            <div class="product-img-placeholder">
              <span class="icon">📦</span>
            </div>
            <h3>{{ p.name }}</h3>
            <div class="price">${{ p.price.toFixed(2) }}</div>
            <button class="btn-primary" @click="addToCart(p)">Add to Cart</button>
          </div>
        </div>
      </section>

      <!-- Cart View -->
      <section v-if="activeTab === 'cart'" class="view-section">
        <h2>Your Cart (Protected API Endpoint)</h2>
        <div v-if="cartItems.length === 0" class="empty-state">
          Your cart is empty. Add items from the Products tab.
        </div>
        <div v-else class="cart-table">
          <div v-for="item in cartItems" :key="item.productId" class="cart-row">
            <span>{{ item.name }} x {{ item.quantity }}</span>
            <span class="price">${{ (item.unitPrice * item.quantity).toFixed(2) }}</span>
          </div>
          <div class="cart-total">
            Total: ${{ cartItems.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0).toFixed(2) }}
          </div>
        </div>
      </section>

      <!-- Email Login View -->
      <section v-if="activeTab === 'login'" class="view-section auth-box">
        <h2>Sign In with Email</h2>
        <div v-if="loginError" class="alert error">{{ loginError }}</div>
        <form @submit.prevent="handleLogin" class="form">
          <div class="form-group">
            <label>Email</label>
            <input v-model="email" type="email" required />
          </div>
          <div class="form-group">
            <label>Password</label>
            <input v-model="password" type="password" required />
          </div>
          <button class="btn-primary" :disabled="loginLoading">
            {{ loginLoading ? 'Authenticating...' : 'Sign In' }}
          </button>
        </form>
      </section>

      <!-- Register View -->
      <section v-if="activeTab === 'register'" class="view-section auth-box">
        <h2>Create an Account</h2>
        <div v-if="regError" class="alert error">{{ regError }}</div>
        <div v-if="regMessage" class="alert success">{{ regMessage }}</div>
        <form @submit.prevent="handleRegister" class="form">
          <div class="form-group">
            <label>Email</label>
            <input v-model="regEmail" type="email" required />
          </div>
          <div class="form-group">
            <label>Password</label>
            <input v-model="regPassword" type="password" required />
          </div>
          <button class="btn-primary" :disabled="regLoading">
            {{ regLoading ? 'Registering...' : 'Register' }}
          </button>
        </form>
      </section>

      <!-- Phone Login View -->
      <section v-if="activeTab === 'phone'" class="view-section auth-box">
        <h2>Passwordless Phone Login</h2>
        <div v-if="phoneError" class="alert error">{{ phoneError }}</div>
        <div v-if="!otpSent">
          <form @submit.prevent="handlePhoneStart" class="form">
            <div class="form-group">
              <label>Phone Number (E.164)</label>
              <input v-model="phoneNumber" type="tel" required />
            </div>
            <button class="btn-primary" :disabled="phoneLoading">
              {{ phoneLoading ? 'Sending...' : 'Send Verification Code' }}
            </button>
          </form>
        </div>
        <div v-else>
          <form @submit.prevent="handlePhoneVerify" class="form">
            <div class="form-group">
              <label>Verification Code</label>
              <input v-model="otpCode" type="text" placeholder="123456" required />
            </div>
            <button class="btn-primary" :disabled="phoneLoading">
              {{ phoneLoading ? 'Verifying...' : 'Verify & Sign In' }}
            </button>
          </form>
        </div>
      </section>
    </main>
  </div>
</template>

<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
  background: #090d16;
  color: #e2e8f0;
  min-height: 100vh;
}
.shop-container { max-width: 1100px; margin: 0 auto; padding: 2rem 1.5rem; }
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 2rem;
  border-bottom: 1px solid #1e293b;
  margin-bottom: 2.5rem;
}
.brand { display: flex; align-items: center; gap: 1rem; }
.logo {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  background: linear-gradient(135deg, #0284c7, #38bdf8);
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 1.5rem;
  color: #fff;
}
h1 { font-size: 1.6rem; font-weight: 700; color: #f8fafc; }
.badge {
  font-size: 0.75rem;
  background: rgba(2, 132, 199, 0.15);
  color: #38bdf8;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  border: 1px solid rgba(56, 189, 248, 0.25);
}
.nav { display: flex; gap: 0.75rem; align-items: center; }
.nav button {
  background: transparent;
  color: #94a3b8;
  border: 1px solid transparent;
  padding: 0.5rem 1rem;
  border-radius: 8px;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.15s ease;
}
.nav button:hover { color: #f8fafc; background: #1e293b; }
.nav button.active { color: #38bdf8; background: #1e293b; border-color: rgba(56, 189, 248, 0.3); }
.user-pill {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  background: #1e293b;
  padding: 0.35rem 0.8rem;
  border-radius: 8px;
}
.user-sub { font-size: 0.85rem; color: #cbd5e1; }
.btn-logout {
  background: #dc2626 !important;
  color: white !important;
  padding: 0.25rem 0.6rem !important;
  font-size: 0.8rem !important;
}
.view-section h2 { font-size: 1.5rem; margin-bottom: 0.5rem; }
.subtitle { color: #64748b; margin-bottom: 2rem; }
.product-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1.5rem;
}
.product-card {
  background: #0f172a;
  border: 1px solid #1e293b;
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  transition: transform 0.15s ease, border-color 0.15s ease;
}
.product-card:hover { transform: translateY(-3px); border-color: #38bdf8; }
.product-img-placeholder {
  height: 120px;
  background: #1e293b;
  border-radius: 8px;
  display: grid;
  place-items: center;
  font-size: 2.5rem;
  margin-bottom: 1rem;
}
.product-card h3 { font-size: 1.05rem; margin-bottom: 0.5rem; flex-grow: 1; }
.price { font-size: 1.25rem; font-weight: 700; color: #38bdf8; margin-bottom: 1rem; }
.btn-primary {
  background: linear-gradient(135deg, #0284c7, #0369a1);
  color: white;
  border: none;
  padding: 0.65rem 1rem;
  border-radius: 8px;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s ease;
  width: 100%;
}
.btn-primary:hover { opacity: 0.9; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.auth-box {
  max-width: 420px;
  margin: 2rem auto;
  background: #0f172a;
  border: 1px solid #1e293b;
  border-radius: 12px;
  padding: 2rem;
}
.form { display: flex; flex-direction: column; gap: 1.25rem; margin-top: 1.25rem; }
.form-group { display: flex; flex-direction: column; gap: 0.4rem; }
.form-group label { font-size: 0.85rem; color: #94a3b8; }
.form-group input {
  background: #1e293b;
  border: 1px solid #334155;
  color: #f8fafc;
  padding: 0.65rem;
  border-radius: 6px;
  font-size: 0.95rem;
}
.form-group input:focus { outline: none; border-color: #38bdf8; }
.alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-bottom: 1rem; }
.alert.error { background: rgba(220, 38, 38, 0.15); border: 1px solid #dc2626; color: #f87171; }
.alert.success { background: rgba(22, 163, 74, 0.15); border: 1px solid #16a34a; color: #4ade80; }
.cart-table {
  background: #0f172a;
  border: 1px solid #1e293b;
  border-radius: 12px;
  padding: 1.5rem;
  max-width: 600px;
}
.cart-row {
  display: flex;
  justify-content: space-between;
  padding: 0.75rem 0;
  border-bottom: 1px solid #1e293b;
}
.cart-total {
  display: flex;
  justify-content: flex-end;
  font-size: 1.2rem;
  font-weight: 700;
  padding-top: 1rem;
  color: #38bdf8;
}
.empty-state { color: #64748b; font-style: italic; }
</style>
